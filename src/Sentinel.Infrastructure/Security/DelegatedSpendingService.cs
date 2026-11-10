using System.Globalization;
using System.Numerics;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Logging;
using Sentinel.Application.DTOs;
using Sentinel.Application.Interfaces;
using Sentinel.Application.Security;
using Sentinel.Domain.Entities;
using Sentinel.Infrastructure.Near;
using Sentinel.Infrastructure.Persistence;

namespace Sentinel.Infrastructure.Security;

public sealed class DelegatedSpendingService(SentinelDbContext db, INearContractService contract,
    ISecurityFactsProvider factsProvider, IDelegationReader reader, DeterministicRiskEngine engine,
    IDelegatedReleaseGate release, IOptions<NearOptions> options, TimeProvider clock, ILogger<DelegatedSpendingService> logger)
{
    private DateTime Now => clock.GetUtcNow().UtcDateTime;
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private string Contract => options.Value.ContractId;
    private static BigInteger N(string s) => BigInteger.Parse(s, CultureInfo.InvariantCulture);
    public static void Id(string id) { if (id is null || !Regex.IsMatch(id, "^[A-Za-z0-9_-]{1,64}$")) throw new ArgumentException("Invalid ID."); }
    private async Task<ContractConfigDto> Actor(string actor, bool owner, CancellationToken ct)
    {
        var c = await contract.GetConfigAsync(ct);
        if (actor != c.Owner && (owner || actor != c.Agent)) throw new UnauthorizedAccessException();
        return c;
    }
    private ContractCallDto Call(string method, object args, ExecutionSecurityDto? security = null) => new(Contract, method, args, "30000000000000", "0", security);
    private void Audit(string type, string actor, string? id, object details) => db.AuditEvents.Add(new() {
        EventType = type, ActorId = actor, PaymentId = id, CreatedAtUtc = Now, DetailsJson = JsonSerializer.Serialize(details) });
    public async Task<ContractCallDto> Create(string owner, CreateDelegationRequest r, CancellationToken ct)
    {
        var c = await Actor(owner, true, ct); Id(r.Id);
        if (c.Paused || r.Agent != c.Agent || r.Recipients is null || r.Recipients.Length is < 1 or > 16 ||
            r.Recipients.Distinct().Count() != r.Recipients.Length) throw new ArgumentException("Invalid delegation authority.");
        foreach (var s in new[] { r.MaxPayment, r.Budget, r.DailyLimit })
            if (s is null || !Regex.IsMatch(s,"^[1-9][0-9]{0,38}$") || N(s) >= BigInteger.One << 128) throw new ArgumentException("Invalid limit.");
        if (N(r.MaxPayment) > N(r.Budget) || N(r.MaxPayment) > N(r.DailyLimit)) throw new ArgumentException("Invalid limits.");
        foreach (var recipient in r.Recipients) {
            new GatewayPaymentRequest(recipient,r.MaxPayment,r.ExpiresAtNs,"Create delegation").Validate(clock.GetUtcNow());
            var f = await factsProvider.FetchAsync(recipient,ct);
            if (!f.Available || f.Owner != owner || f.Agent != r.Agent || f.ContractId != Contract || f.Paused || !f.RecipientAllowed ||
                !f.RecipientExists || N(r.MaxPayment)>N(f.MaxPerTransaction) || N(r.DailyLimit)>N(f.DailyLimit))
                throw new GatewayRejectedException("DELEGATION_POLICY_REJECTED");
            if (await reader.ReadAsync(r.Id,f.BlockHash,ct) is not null) throw new GatewayRejectedException("DELEGATION_ID_USED");
        }
        // This is a wallet preparation only, never proof of an on-chain delegation.
        if (!await db.Delegations.AnyAsync(x=>x.Id==r.Id && x.ContractId==Contract,ct))
            db.Delegations.Add(new() { Id=r.Id, ContractId=Contract, OwnerId=owner, ObservedAtUtc=Now });
        Audit("DelegationCreationPrepared",owner,null,r); await db.SaveChangesAsync(ct);
        return Call("create_delegation",new {id=r.Id,agent=r.Agent,max_payment=r.MaxPayment,budget=r.Budget,
            daily_limit=r.DailyLimit,expires_at_ns=r.ExpiresAtNs,recipients=r.Recipients});
    }
    public async Task<object> Inspect(string actor,string id,CancellationToken ct)
    {
        Id(id); var c=await Actor(actor,false,ct);
        // Obtain a fresh finalized block before reading authority.
        var f=await factsProvider.FetchAsync(c.Owner,ct);
        if (!f.Available || f.Owner!=c.Owner || f.Agent!=c.Agent || f.ContractId!=Contract) throw new GatewayRejectedException("AUTHORITY_UNAVAILABLE");
        var state=await reader.ReadAsync(id,f.BlockHash,ct);
        var record=await db.Delegations.SingleOrDefaultAsync(x=>x.Id==id && x.ContractId==Contract,ct);
        if(record is null) { record=new() {Id=id,ContractId=Contract,OwnerId=c.Owner};db.Delegations.Add(record); }
        record.StateJson=JsonSerializer.Serialize(state,Json);record.ObservedAtUtc=Now;await db.SaveChangesAsync(ct);
        return new {id, state, observedAtUtc=record.ObservedAtUtc, blockHash=f.BlockHash,
            remainingBudget=state is null ? "0" : BigInteger.Max(0,N(state.Budget)-N(state.Spent)).ToString(),
            payments=await db.DelegatedPayments.AsNoTracking().Where(x=>x.ContractId==Contract && x.DelegationId==id).OrderByDescending(x=>x.CreatedAtUtc).Take(100).ToListAsync(ct)};
    }
    public async Task<object> List(string actor,CancellationToken ct) {
        await Actor(actor,false,ct);
        return await db.Delegations.AsNoTracking().Where(x=>x.ContractId==Contract).OrderBy(x=>x.Id).Take(100).ToListAsync(ct);
    }
    public async Task<ContractCallDto> Revoke(string owner,string id,CancellationToken ct) {
        await Actor(owner,true,ct);Id(id);Audit("DelegationRevocationPrepared",owner,null,new{id});await db.SaveChangesAsync(ct);
        return Call("revoke_delegation",new{id});
    }
    public async Task<DelegatedPaymentRecord> Propose(string actor,DelegatedProposalRequest r,CancellationToken ct,Guid? runId=null) {
        var c=await Actor(actor,false,ct);Id(r.DelegationId);r.Intent.Validate(clock.GetUtcNow());
        if(runId is not null) {
            var run=await db.AuditEvents.AsNoTracking().SingleOrDefaultAsync(x=>x.Id==runId && x.ActorId==c.Owner && x.EventType=="DelegatedAgentRunStarted",ct)
                ?? throw new GatewayRejectedException("AGENT_RUN_INVALID");
            var constraints=JsonSerializer.Deserialize<JsonElement>(run.DetailsJson!);
            if(constraints.GetProperty("DelegationId").GetString()!=r.DelegationId || constraints.GetProperty("Recipient").GetString()!=r.Intent.Recipient ||
                N(r.Intent.AmountYocto)>N(constraints.GetProperty("MaxAmountYocto").GetString()!))throw new GatewayRejectedException("TASK_SPENDING_CONSTRAINT");
            if(await db.AuditEvents.AnyAsync(x=>x.Id==AgentRunBinding.Id(runId.Value),ct))throw new GatewayRejectedException("AGENT_ASSESSMENT_ALREADY_BOUND");
        }
        var p=new DelegatedPaymentRecord {DelegationId=r.DelegationId,ContractId=Contract,OwnerId=c.Owner,AgentId=c.Agent,
            IntentJson=JsonSerializer.Serialize(r.Intent),CreatedAtUtc=Now};
        p.IntentHash=GatewayPaymentRequest.HashText(JsonSerializer.Serialize(new {network="testnet",contract=Contract,method="execute_delegated_payment",
            agent=c.Agent,delegationId=p.DelegationId,paymentId=p.Id,intent=r.Intent}));
        var evaluation=await Evaluate(p,ct);p.Decision=evaluation.Risk.Decision;p.DecisionJson=JsonSerializer.Serialize(evaluation.Risk,Json);p.Fingerprint=evaluation.Risk.Fingerprint;
        db.DelegatedPayments.Add(p);Audit("DelegatedProposalAssessed",actor,p.Id,new{p.Decision,p.Fingerprint,p.IntentHash});
        if(runId is not null)db.AuditEvents.Add(new(){Id=AgentRunBinding.Id(runId.Value),EventType="DelegatedAgentProposalBound",ActorId=c.Owner,PaymentId=p.Id,CreatedAtUtc=Now,DetailsJson=JsonSerializer.Serialize(new{runId,paymentId=p.Id})});
        await db.SaveChangesAsync(ct);return p;
    }
    public async Task<DelegatedPaymentRecord> Get(string actor,string id,CancellationToken ct) { await Actor(actor,false,ct);return await Payment(id,ct); }
    private async Task<DelegatedPaymentRecord> Payment(string id,CancellationToken ct) {
        Id(id);return await db.DelegatedPayments.AsNoTracking().SingleOrDefaultAsync(x=>x.Id==id && x.ContractId==Contract,ct) ?? throw new KeyNotFoundException();
    }
    private async Task<(RiskDecision Risk,SecurityFacts Facts)> Evaluate(DelegatedPaymentRecord p,CancellationToken ct) {
        var c=await contract.GetConfigAsync(ct);var intent=JsonSerializer.Deserialize<GatewayPaymentRequest>(p.IntentJson)!;intent.Validate(clock.GetUtcNow());
        var f=await factsProvider.FetchAsync(intent.Recipient,ct);
        if(!f.Available || f.ContractId!=Contract || f.Owner!=c.Owner || f.Agent!=c.Agent || c.Owner!=p.OwnerId || c.Agent!=p.AgentId)
            throw new GatewayRejectedException("DELEGATION_AUTHORITY_CHANGED_OR_UNAVAILABLE");
        var d=await reader.ReadAsync(p.DelegationId,f.BlockHash,ct) ?? throw new GatewayRejectedException("DELEGATION_NOT_FOUND");
        var failures=DelegationConstraints.Failures(d,c.Agent,intent,clock.GetUtcNow());
        if(failures.Length>0) logger.LogWarning("Delegation validation blocked payment {PaymentId} delegation {DelegationId}: {FailureCodes}",p.Id,p.DelegationId,string.Join(",",failures));
        var entries=await db.CounterpartyBlocklist.AsNoTracking().Where(x=>x.AccountId==intent.Recipient && x.RevokedAtUtc==null && (x.ExpiresAtUtc==null || x.ExpiresAtUtc>Now)).ToListAsync(ct);
        var evidence=entries.Select(x=>new RiskEvidence("COUNTERPARTY_BLOCKLISTED",x.EvidenceUri,x.CreatedAtUtc,null,x.EvidenceSha256)).ToArray();
        var risk=engine.Evaluate(intent,f,"Authorization",evidence,clock.GetUtcNow());
        if(failures.Length>0) risk=risk with {Decision="BLOCK",Score=100,ReasonCodes=risk.ReasonCodes.Append("DELEGATION_LIMIT_OR_AUTHORITY").Concat(failures).ToArray()};
        // Bind the complete finalized delegation snapshot. Changed usage requires reassessment.
        var authority=JsonSerializer.Serialize(new {delegation=d,risk.Decision,risk.ReasonCodes});
        risk=risk with {Fingerprint=GatewayPaymentRequest.HashText(p.IntentHash+":"+risk.Fingerprint+":"+authority)};
        return(risk,f);
    }
    public async Task<DelegatedPaymentRecord> Reassess(string actor,string id,CancellationToken ct) {
        await Actor(actor,false,ct);var p=await Payment(id,ct);var fresh=await Evaluate(p,ct);
        var count=await db.DelegatedPayments.Where(x=>x.Id==id && x.ContractId==Contract && x.PermitId==null)
            .ExecuteUpdateAsync(s=>s.SetProperty(x=>x.Decision,fresh.Risk.Decision).SetProperty(x=>x.DecisionJson,JsonSerializer.Serialize(fresh.Risk,Json))
                .SetProperty(x=>x.Fingerprint,fresh.Risk.Fingerprint).SetProperty(x=>x.ApprovedAtUtc,(DateTime?)null)
                .SetProperty(x=>x.ApprovalExpiresAtUtc,(DateTime?)null).SetProperty(x=>x.ApprovalFingerprint,(string?)null),ct);
        if(count!=1)throw new GatewayRejectedException("EXECUTION_ALREADY_CLAIMED");
        return await Payment(id,ct);
    }
    public async Task Approve(string owner,string id,ApproveAssessmentRequest r,CancellationToken ct) {
        await Actor(owner,true,ct);var p=await Payment(id,ct);var fresh=await Evaluate(p,ct);
        if(!r.Confirm || fresh.Risk.Decision!="WARN" || p.Fingerprint!=r.Fingerprint || fresh.Risk.Fingerprint!=r.Fingerprint || p.ConsumedAtUtc!=null)
            throw new GatewayRejectedException("APPROVAL_INVALID_OR_CHANGED");
        var count=await db.DelegatedPayments.Where(x=>x.Id==id && x.ContractId==Contract && x.ConsumedAtUtc==null && x.PermitId==null)
            .ExecuteUpdateAsync(s=>s.SetProperty(x=>x.ApprovedAtUtc,Now).SetProperty(x=>x.ApprovalExpiresAtUtc,clock.GetUtcNow().AddMinutes(2).UtcDateTime)
                .SetProperty(x=>x.ApprovalFingerprint,r.Fingerprint),ct);
        if(count!=1)throw new GatewayRejectedException("APPROVAL_ALREADY_CLAIMED");
        Audit("DelegatedHumanApproval",owner,id,new{r.Fingerprint});await db.SaveChangesAsync(ct);
    }
    private void RequireAllowed(DelegatedPaymentRecord p,RiskDecision r) {
        if(p.Decision=="BLOCK" || r.Decision=="BLOCK")throw new GatewayRejectedException("SECURITY_BLOCK");
        if(p.Fingerprint!=r.Fingerprint)throw new GatewayRejectedException("ASSESSMENT_CHANGED_REASSESS_REQUIRED");
        if(r.Decision!="ALLOW" && !(r.Decision=="WARN" && p.ApprovedAtUtc!=null && p.ApprovalExpiresAtUtc>Now && p.ApprovalFingerprint==r.Fingerprint))
            throw new GatewayRejectedException("HUMAN_APPROVAL_REQUIRED");
    }
    private ContractCallDto PaymentCall(DelegatedPaymentRecord p,ExecutionSecurityDto? permit=null) {
        var i=JsonSerializer.Deserialize<GatewayPaymentRequest>(p.IntentJson)!;
        return Call("execute_delegated_payment",new{delegation_id=p.DelegationId,id=p.Id,recipient=i.Recipient,amount_yocto=i.AmountYocto,expires_at_ns=i.ExpiresAtNs},permit);
    }
    public async Task RequireOwner(string actor,CancellationToken ct) { await Actor(actor,true,ct); }
    public async Task Preflight(string actor,string id,CancellationToken ct) {
        await Actor(actor,false,ct);var p=await Payment(id,ct);
        if(p.PermitId!=null || p.ConsumedAtUtc!=null || p.TransactionHash!=null)throw new GatewayRejectedException("EXECUTION_ALREADY_CLAIMED_RECONCILE_REQUIRED");
        var fresh=await Evaluate(p,ct);RequireAllowed(p,fresh.Risk);
        await release.VerifyAsync(fresh.Facts,ct);
    }
    public async Task<ContractCallDto> Prepare(string id,CancellationToken ct) {
        var p=await Payment(id,ct);var fresh=await Evaluate(p,ct);RequireAllowed(p,fresh.Risk);
        var key=await release.VerifyAsync(fresh.Facts,ct);var permit=Guid.NewGuid();
        var intent=JsonSerializer.Deserialize<GatewayPaymentRequest>(p.IntentJson)!;
        var intentExpiry=DateTimeOffset.FromUnixTimeMilliseconds((long)(N(intent.ExpiresAtNs)/1_000_000)).UtcDateTime;
        var expiry=new[]{clock.GetUtcNow().AddSeconds(20).UtcDateTime,intentExpiry}.Min();
        // No permit reissue: ambiguous execution requires reconciliation, never automatic signing twice.
        var count=await db.DelegatedPayments.Where(x=>x.Id==id && x.ContractId==Contract && x.PermitId==null && x.ConsumedAtUtc==null)
            .ExecuteUpdateAsync(s=>s.SetProperty(x=>x.PermitId,permit).SetProperty(x=>x.PermitExpiresAtUtc,expiry)
                .SetProperty(x=>x.PermitFingerprint,fresh.Risk.Fingerprint).SetProperty(x=>x.SignerPublicKey,key),ct);
        if(count!=1)throw new GatewayRejectedException("EXECUTION_ALREADY_CLAIMED");
        return PaymentCall(p,new(permit,p.IntentHash,expiry,key));
    }
    public async Task<ConsumedPermitDto> Consume(string id,ConsumePermitRequest r,CancellationToken ct) {
        var p=await Payment(id,ct);
        if(p.PermitId!=r.PermitId || p.IntentHash!=r.IntentHash || p.PermitExpiresAtUtc<=Now || p.PermitExpiresAtUtc==null || p.ConsumedAtUtc!=null)
            throw new GatewayRejectedException("PERMIT_USED_CHANGED_OR_EXPIRED");
        var fresh=await Evaluate(p,ct);RequireAllowed(p,fresh.Risk);var key=await release.VerifyAsync(fresh.Facts,ct);
        if(p.PermitFingerprint!=fresh.Risk.Fingerprint || key!=p.SignerPublicKey)throw new GatewayRejectedException("PERMIT_CONTEXT_CHANGED");
        var count=await db.DelegatedPayments.Where(x=>x.Id==id && x.ContractId==Contract && x.PermitId==r.PermitId && x.ConsumedAtUtc==null && x.PermitExpiresAtUtc>Now)
            .ExecuteUpdateAsync(s=>s.SetProperty(x=>x.ConsumedAtUtc,Now).SetProperty(x=>x.Status,"Attempted"),ct);
        if(count!=1)throw new GatewayRejectedException("PERMIT_ALREADY_USED");
        Audit("DelegatedPermitConsumed",p.AgentId,id,new{p.IntentHash,r.PermitId});await db.SaveChangesAsync(ct);
        return new(r.PermitId,id,p.IntentHash,fresh.Risk.Decision,fresh.Risk.Decision=="WARN",p.PermitExpiresAtUtc.Value,key,PaymentCall(p));
    }
    public async Task<object> Reconcile(string actor,string id,CancellationToken ct) {
        await Actor(actor,false,ct);var p=await Payment(id,ct);var i=JsonSerializer.Deserialize<GatewayPaymentRequest>(p.IntentJson)!;
        var chain=await contract.GetPaymentAsync(id,ct);
        if(chain is not null) {
            var facts=await factsProvider.FetchAsync(i.Recipient,ct);
            if(!facts.Available || await reader.PaymentDelegationAsync(id,facts.BlockHash,ct)!=p.DelegationId)
                throw new GatewayRejectedException("ONCHAIN_DELEGATION_CHANGED_OR_UNAVAILABLE");
        }
        if(chain is not null && (chain.Recipient!=i.Recipient || chain.AmountYocto!=i.AmountYocto || chain.ExpiresAtNs!=ulong.Parse(i.ExpiresAtNs)))
            throw new GatewayRejectedException("ONCHAIN_INTENT_CHANGED");
        var status=chain?.Status.ToString() switch {"Executing"=>"Reserved","Completed"=>"Settled","Failed"=>"Failed","Cancelled"=>"Cancelled",_=>p.Status};
        await db.DelegatedPayments.Where(x=>x.Id==id && x.ContractId==Contract).ExecuteUpdateAsync(s=>s.SetProperty(x=>x.Status,status),ct);
        return new {paymentId=id,status,chainObserved=chain is not null,transactionHash=p.TransactionHash};
    }
    public async Task Report(string id,string hash,CancellationToken ct) {
        if(hash is null || !Regex.IsMatch(hash,"^[1-9A-HJ-NP-Za-km-z]{32,64}$"))throw new ArgumentException("Invalid transaction hash.");
        var p=await Payment(id,ct);if(p.ConsumedAtUtc==null)throw new GatewayRejectedException("PERMIT_NOT_CONSUMED");
        // Worker claims are unverified evidence only. Settlement comes exclusively from chain reconciliation.
        if(p.TransactionHash!=null && p.TransactionHash!=hash)throw new GatewayRejectedException("TRANSACTION_ALREADY_REPORTED");
        await db.DelegatedPayments.Where(x=>x.Id==id && x.ContractId==Contract && x.TransactionHash==null)
            .ExecuteUpdateAsync(s=>s.SetProperty(x=>x.TransactionHash,hash),ct);
        Audit("DelegatedTransactionReportedUnverified",p.AgentId,id,new{hash});await db.SaveChangesAsync(ct);
    }
}
