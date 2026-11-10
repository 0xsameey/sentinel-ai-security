using System.Data;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Sentinel.Application.DTOs;
using Sentinel.Application.Interfaces;
using Sentinel.Application.Security;
using Sentinel.Domain.Entities;
using Sentinel.Domain.Enums;
using Sentinel.Infrastructure.Near;
using Sentinel.Infrastructure.Persistence;

namespace Sentinel.Infrastructure.Security;

public sealed class SecurityGateway(SentinelDbContext db, ISecurityFactsProvider provider, DeterministicRiskEngine engine,
    INearContractService contract, IOptions<NearOptions> options, IExecutionReleaseGate release, TimeProvider clock) : ISecurityGateway
{
    private DateTime Now => clock.GetUtcNow().UtcDateTime;
    private async Task<ContractConfigDto> ActorAsync(string actor, bool ownerOnly, CancellationToken ct)
    {
        var config = await contract.GetConfigAsync(ct);
        if (actor != config.Owner && (ownerOnly || actor != config.Agent)) throw new UnauthorizedAccessException();
        return config;
    }
    public async Task<GatewayAssessmentDto> AssessAsync(string actor, GatewayPaymentRequest request, string stage = "Authorization", string? paymentId = null, CancellationToken ct = default)
    {
        request.Validate(clock.GetUtcNow());
        var config = await ActorAsync(actor, false, ct);
        if (stage is not ("Authorization" or "Execution")) throw new ArgumentException("Invalid stage.");
        if (stage == "Execution")
        {
            var record = await PaymentAsync(paymentId ?? "", ct);
            if (request != Intent(record)) throw new GatewayRejectedException("INTENT_CHANGED");
            await AssertOnChainAsync(record, ct);
        }
        return (await EvaluateAsync(actor, config, request, stage, paymentId, ct)).Assessment;
    }
    private async Task<(GatewayAssessmentDto Assessment, SecurityFacts Facts)> EvaluateAsync(string actor, ContractConfigDto config,
        GatewayPaymentRequest request, string stage, string? paymentId, CancellationToken ct)
    {
        var facts = await provider.FetchAsync(request.Recipient, ct);
        // Never accept an independent snapshot that changed the authority being used.
        if (facts.Available && (facts.Owner != config.Owner || facts.Agent != config.Agent || facts.ContractId != options.Value.ContractId))
            throw new GatewayRejectedException("AUTHORITY_CHANGED");
        var now = Now;
        var entries = await db.CounterpartyBlocklist.AsNoTracking().Where(x => x.AccountId == request.Recipient &&
            x.RevokedAtUtc == null && (x.ExpiresAtUtc == null || x.ExpiresAtUtc > now)).OrderBy(x => x.Id).ToListAsync(ct);
        var evidence = entries.Select(x => new RiskEvidence("COUNTERPARTY_BLOCKLISTED", x.EvidenceUri, x.CreatedAtUtc, null,
            $"entry={x.Id};reason={x.ReasonCode};sha256={x.EvidenceSha256}")).ToArray();
        var decision = engine.Evaluate(request, facts, stage, evidence, clock.GetUtcNow());
        var record = new SecurityAssessmentRecord { OwnerId = config.Owner, ActorId = actor, ContractId = options.Value.ContractId,
            Stage = stage, PaymentId = paymentId, IntentHash = request.Hash(options.Value.ContractId, config.Owner),
            IntentJson = JsonSerializer.Serialize(request), DecisionJson = JsonSerializer.Serialize(decision), Decision = decision.Decision,
            Fingerprint = decision.Fingerprint, CreatedAtUtc = now, ExpiresAtUtc = decision.ExpiresAtUtc.UtcDateTime };
        db.SecurityAssessments.Add(record);
        Audit("SecurityAssessmentRecorded", actor, paymentId, new { assessmentId = record.Id, stage,
            decision = decision.Decision, recipient = request.Recipient, decision.ReasonCodes, decision.Fingerprint });
        await db.SaveChangesAsync(ct);
        return (ToDto(record), facts);
    }
    public async Task<GatewayAssessmentDto> GetAsync(string actor, Guid assessmentId, CancellationToken ct = default)
    {
        await ActorAsync(actor, false, ct); return ToDto(await AssessmentAsync(assessmentId, ct));
    }
    public async Task<HumanApprovalDto> ApproveAsync(string owner, Guid assessmentId, ApproveAssessmentRequest request, CancellationToken ct = default)
    {
        var config = await ActorAsync(owner, true, ct);
        var original = await AssessmentAsync(assessmentId, ct);
        if (!request.Confirm || original.OwnerId != owner || original.Decision != "WARN" ||
            original.ExpiresAtUtc <= Now || request.Fingerprint != original.Fingerprint)
            throw new GatewayRejectedException("APPROVAL_INVALID_OR_EXPIRED");
        var intent = JsonSerializer.Deserialize<GatewayPaymentRequest>(original.IntentJson)!;
        intent.Validate(clock.GetUtcNow());
        if (original.Stage == "Execution") await AssertOnChainAsync(await PaymentAsync(original.PaymentId!, ct), ct);
        var fresh = await EvaluateAsync(owner, config, intent, original.Stage, original.PaymentId, ct);
        if (fresh.Assessment.Risk.Decision != "WARN" || fresh.Assessment.Risk.Fingerprint != original.Fingerprint)
            throw new GatewayRejectedException("APPROVAL_CONTEXT_CHANGED", fresh.Assessment);
        if (await db.HumanApprovals.AnyAsync(x => x.AssessmentId == assessmentId, ct)) throw new GatewayRejectedException("APPROVAL_ALREADY_GRANTED");
        var approval = new HumanApprovalRecord { AssessmentId = assessmentId, OwnerId = owner, Stage = original.Stage,
            PaymentId = original.PaymentId, IntentHash = original.IntentHash, Fingerprint = original.Fingerprint,
            CreatedAtUtc = Now, ExpiresAtUtc = original.ExpiresAtUtc };
        db.HumanApprovals.Add(approval);
        Audit("HumanRiskApprovalGranted", owner, original.PaymentId, new { assessmentId, approval.Id, original.Fingerprint });
        await db.SaveChangesAsync(ct);
        return new(approval.Id, assessmentId, approval.Fingerprint, approval.ExpiresAtUtc);
    }
    public async Task<GatewayAssessmentDto> AuthorizePreparationAsync(string owner, GatewayPaymentRequest request,
        Guid? assessmentId, Guid? approvalId, string paymentId, CancellationToken ct = default)
    {
        var config = await ActorAsync(owner, true, ct); request.Validate(clock.GetUtcNow());
        // Reject expired references independently of provider availability.
        if (assessmentId is not null && (await AssessmentAsync(assessmentId.Value, ct)).ExpiresAtUtc <= Now)
            throw new GatewayRejectedException("ASSESSMENT_CHANGED_OR_EXPIRED");
        var fresh = (await EvaluateAsync(owner, config, request, "Authorization", null, ct)).Assessment;
        if (fresh.Risk.Decision == "BLOCK") throw new GatewayRejectedException("SECURITY_BLOCK", fresh);
        SecurityAssessmentRecord? original = null;
        if (assessmentId is not null)
        {
            original = await AssessmentAsync(assessmentId.Value, ct);
            if (original.OwnerId != owner || original.Stage != "Authorization" || original.IntentHash != fresh.IntentHash ||
                original.ExpiresAtUtc <= Now || original.Fingerprint != fresh.Risk.Fingerprint)
                throw new GatewayRejectedException("ASSESSMENT_CHANGED_OR_EXPIRED", fresh);
        }
        if (fresh.Risk.Decision == "WARN")
        {
            if (original is null || approvalId is null) throw new GatewayRejectedException("HUMAN_APPROVAL_REQUIRED", fresh);
            var approval = await ValidApprovalAsync(original.Id, approvalId.Value, fresh, owner, paymentId, true, ct);
            if (approval.ConsumedAtUtc is null)
            {
                var count = await db.HumanApprovals.Where(x => x.Id == approval.Id && x.ConsumedAtUtc == null && x.ExpiresAtUtc > Now)
                    .ExecuteUpdateAsync(s => s.SetProperty(x => x.ConsumedAtUtc, Now).SetProperty(x => x.ConsumedByPaymentId, paymentId), ct);
                if (count != 1) throw new GatewayRejectedException("APPROVAL_ALREADY_USED");
            }
        }
        return fresh;
    }
    public async Task PreflightExecutionAsync(string actor, string paymentId, CancellationToken ct = default)
    {
        var config = await ActorAsync(actor, false, ct);
        var payment = await PaymentAsync(paymentId, ct);
        if (payment.OwnerAccountId != config.Owner) throw new GatewayRejectedException("OWNER_CHANGED");
        await ExecutionAssessmentAsync(config.Agent, config, payment, ct);
    }
    public async Task<ContractCallDto> PrepareExecutionAsync(string agent, string paymentId, CancellationToken ct = default)
    {
        var config = await ActorAsync(agent, false, ct);
        if (agent != config.Agent) throw new UnauthorizedAccessException();
        var payment = await PaymentAsync(paymentId, ct);
        var (assessment, facts, approval) = await ExecutionAssessmentAsync(agent, config, payment, ct);
        var publicKey = await release.VerifyAsync(facts, ct);
        // Release attestations also run in ExecutionAssessment; check again at consumption.
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var now = Now;
        var existing = await db.ExecutionPermits.SingleOrDefaultAsync(x => x.PaymentId == paymentId, ct);
        if (existing is not null)
        {
            if (existing.ConsumedAtUtc is not null || existing.ExpiresAtUtc > now) throw new GatewayRejectedException("EXECUTION_ALREADY_CLAIMED");
            db.ExecutionPermits.Remove(existing); await db.SaveChangesAsync(ct);
        }
        var permit = new ExecutionPermitRecord { PaymentId = paymentId, AssessmentId = assessment.AssessmentId, ApprovalId = approval?.Id,
            IntentHash = assessment.IntentHash, Fingerprint = assessment.Risk.Fingerprint, SignerPublicKey = publicKey,
            ExpiresAtUtc = clock.GetUtcNow().AddSeconds(20).UtcDateTime };
        db.ExecutionPermits.Add(permit); Audit("ExecutionPermitIssued", agent, paymentId, new { permit.PermitId, permit.IntentHash });
        await db.SaveChangesAsync(ct); await tx.CommitAsync(ct);
        return Call(paymentId, new(permit.PermitId, permit.IntentHash, permit.ExpiresAtUtc, publicKey));
    }
    public async Task<ConsumedPermitDto> ConsumeAsync(string paymentId, ConsumePermitRequest request, CancellationToken ct = default)
    {
        var permit = await db.ExecutionPermits.AsNoTracking().SingleOrDefaultAsync(x => x.PaymentId == paymentId && x.PermitId == request.PermitId, ct)
            ?? throw new GatewayRejectedException("PERMIT_INVALID");
        if (permit.ConsumedAtUtc is not null || permit.ExpiresAtUtc <= Now || request.IntentHash != permit.IntentHash)
            throw new GatewayRejectedException("PERMIT_USED_CHANGED_OR_EXPIRED");
        var config = await contract.GetConfigAsync(ct);
        var payment = await PaymentAsync(paymentId, ct);
        var (assessment, facts, approval) = await ExecutionAssessmentAsync(config.Agent, config, payment, ct);
        var publicKey = await release.VerifyAsync(facts, ct);
        if (assessment.Risk.Fingerprint != permit.Fingerprint || assessment.IntentHash != permit.IntentHash ||
            publicKey != permit.SignerPublicKey || approval?.Id != permit.ApprovalId)
            throw new GatewayRejectedException("PERMIT_CONTEXT_CHANGED");
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var changed = await db.ExecutionPermits.Where(x => x.PaymentId == paymentId && x.PermitId == request.PermitId &&
            x.ConsumedAtUtc == null && x.ExpiresAtUtc > Now).ExecuteUpdateAsync(s => s.SetProperty(x => x.ConsumedAtUtc, Now), ct);
        if (changed != 1) throw new GatewayRejectedException("PERMIT_ALREADY_USED");
        if (approval is not null)
        {
            var used = await db.HumanApprovals.Where(x => x.Id == approval.Id && x.ConsumedAtUtc == null && x.ExpiresAtUtc > Now)
                .ExecuteUpdateAsync(s => s.SetProperty(x => x.ConsumedAtUtc, Now).SetProperty(x => x.ConsumedByPaymentId, paymentId), ct);
            if (used != 1) throw new GatewayRejectedException("APPROVAL_ALREADY_USED");
        }
        Audit("ExecutionPermitConsumed", config.Agent, paymentId, new { permit.PermitId, permit.IntentHash });
        await db.SaveChangesAsync(ct); await tx.CommitAsync(ct);
        return new(permit.PermitId, paymentId, permit.IntentHash, assessment.Risk.Decision, approval is not null,
            permit.ExpiresAtUtc, publicKey, Call(paymentId));
    }
    private async Task<(GatewayAssessmentDto Assessment, SecurityFacts Facts, HumanApprovalRecord? Approval)> ExecutionAssessmentAsync(
        string agent, ContractConfigDto config, PaymentRequestRecord payment, CancellationToken ct)
    {
        if (payment.OwnerAccountId != config.Owner) throw new GatewayRejectedException("OWNER_CHANGED");
        await AssertOnChainAsync(payment, ct);
        var intent = Intent(payment).Validate(clock.GetUtcNow());
        if (payment.SecurityAssessmentId is null || payment.SecurityIntentHash != intent.Hash(options.Value.ContractId, config.Owner) ||
            payment.RiskDecision is not ("ALLOW" or "WARN")) throw new GatewayRejectedException("APPROVED_INTENT_REQUIRED");
        var authorization = await AssessmentAsync(payment.SecurityAssessmentId.Value, ct);
        if (authorization.OwnerId != config.Owner || authorization.Stage != "Authorization" ||
            authorization.IntentHash != payment.SecurityIntentHash || authorization.Decision is not ("ALLOW" or "WARN"))
            throw new GatewayRejectedException("APPROVED_INTENT_REQUIRED");
        if (payment.RiskDecision == "WARN" && !await db.HumanApprovals.AsNoTracking().AnyAsync(x =>
            x.Id == payment.SecurityApprovalId && x.AssessmentId == authorization.Id && x.Stage == "Authorization" &&
            x.OwnerId == config.Owner && x.IntentHash == payment.SecurityIntentHash && x.Fingerprint == authorization.Fingerprint &&
            x.ConsumedAtUtc != null && x.ConsumedByPaymentId == payment.PaymentId, ct))
            throw new GatewayRejectedException("APPROVED_INTENT_REQUIRED");
        var fresh = await EvaluateAsync(agent, config, intent, "Execution", payment.PaymentId, ct);
        if (fresh.Assessment.Risk.Decision == "BLOCK") throw new GatewayRejectedException("SECURITY_BLOCK", fresh.Assessment);
        HumanApprovalRecord? approval = null;
        if (fresh.Assessment.Risk.Decision == "WARN")
        {
            var now = Now;
            approval = await db.HumanApprovals.AsNoTracking().Where(x => x.PaymentId == payment.PaymentId && x.Stage == "Execution" &&
                x.OwnerId == config.Owner && x.IntentHash == fresh.Assessment.IntentHash && x.Fingerprint == fresh.Assessment.Risk.Fingerprint &&
                x.ConsumedAtUtc == null && x.ExpiresAtUtc > now).OrderBy(x => x.CreatedAtUtc).FirstOrDefaultAsync(ct);
            if (approval is null) throw new GatewayRejectedException("HUMAN_APPROVAL_REQUIRED", fresh.Assessment);
        }
        await release.VerifyAsync(fresh.Facts, ct);
        return (fresh.Assessment, fresh.Facts, approval);
    }
    private async Task<HumanApprovalRecord> ValidApprovalAsync(Guid assessmentId, Guid id, GatewayAssessmentDto fresh, string owner,
        string paymentId, bool allowIdempotent, CancellationToken ct)
    {
        var approval = await db.HumanApprovals.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id, ct)
            ?? throw new GatewayRejectedException("APPROVAL_NOT_FOUND");
        if (approval.AssessmentId != assessmentId || approval.Stage != fresh.Stage || approval.OwnerId != owner ||
            approval.IntentHash != fresh.IntentHash || approval.Fingerprint != fresh.Risk.Fingerprint || approval.ExpiresAtUtc <= Now ||
            (approval.ConsumedAtUtc is not null && (!allowIdempotent || approval.ConsumedByPaymentId != paymentId)))
            throw new GatewayRejectedException("APPROVAL_INVALID_OR_EXPIRED");
        return approval;
    }
    private async Task AssertOnChainAsync(PaymentRequestRecord record, CancellationToken ct)
    {
        var payment = await contract.GetPaymentAsync(record.PaymentId, ct);
        if (payment is null || payment.Status != PaymentStatus.Authorized || payment.Recipient != record.Recipient ||
            payment.AmountYocto != record.AmountYocto || payment.ExpiresAtNs != record.ExpiresAtNs)
            throw new GatewayRejectedException("ONCHAIN_INTENT_NOT_AUTHORIZED_OR_CHANGED");
    }
    private async Task<PaymentRequestRecord> PaymentAsync(string id, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(id) || id.Length > 64) throw new ArgumentException("Invalid payment ID.");
        return await db.PaymentRequests.AsNoTracking().SingleOrDefaultAsync(x => x.PaymentId == id && x.ContractId == options.Value.ContractId, ct)
            ?? throw new KeyNotFoundException("Recorded intent not found.");
    }
    private async Task<SecurityAssessmentRecord> AssessmentAsync(Guid id, CancellationToken ct) =>
        await db.SecurityAssessments.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id && x.ContractId == options.Value.ContractId, ct)
        ?? throw new KeyNotFoundException("Assessment not found.");
    private static GatewayPaymentRequest Intent(PaymentRequestRecord p) => new(p.Recipient, p.AmountYocto, p.ExpiresAtNs.ToString(System.Globalization.CultureInfo.InvariantCulture), p.Purpose);
    private ContractCallDto Call(string id, ExecutionSecurityDto? security = null) => new(options.Value.ContractId, "execute_payment", new { id }, "30000000000000", "0", security);
    private static GatewayAssessmentDto ToDto(SecurityAssessmentRecord r) => new(r.Id, r.IntentHash, r.Stage, r.PaymentId,
        JsonSerializer.Deserialize<GatewayPaymentRequest>(r.IntentJson)!, JsonSerializer.Deserialize<RiskDecision>(r.DecisionJson)!);
    private void Audit(string type, string actor, string? paymentId, object details) => db.AuditEvents.Add(new() {
        EventType = type, ActorId = actor, PaymentId = paymentId, CreatedAtUtc = Now, DetailsJson = JsonSerializer.Serialize(details) });
}
