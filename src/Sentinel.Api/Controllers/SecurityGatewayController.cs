using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Sentinel.Api.Authentication;
using Sentinel.Application.Interfaces;
using Sentinel.Application.Security;
using Sentinel.Domain.Entities;
using Sentinel.Infrastructure.Persistence;

namespace Sentinel.Api.Controllers;

[ApiController]
[Route("api/v1/gateway")]
[Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme + "," + GatewayAgentHandler.SchemeName)]
[EnableRateLimiting("gateway")]
[RequestSizeLimit(8192)]
public sealed class SecurityGatewayController(ISecurityGateway gateway, IPaymentTrackingService tracking,
    INearContractService contract, SentinelDbContext db, TimeProvider clock, ISecurityFactsProvider facts) : ControllerBase
{
    private string Actor => User.FindFirst("sub")?.Value ?? throw new UnauthorizedAccessException();
    [HttpGet("context/{recipient}")]
    public async Task<IActionResult> Context(string recipient, CancellationToken ct)
    {
        var config = await contract.GetConfigAsync(ct);
        if (Actor != config.Owner && Actor != config.Agent) throw new UnauthorizedAccessException();
        if (!GatewayPaymentRequest.ValidAccount(recipient)) throw new ArgumentException("Invalid recipient.");
        var snapshot = await facts.FetchAsync(recipient, ct);
        if (!snapshot.Available) return StatusCode(503, new { code = "CRITICAL_RPC_UNAVAILABLE" });
        return Ok(new { network = "testnet", snapshot.ContractId, snapshot.Paused, recipient,
            snapshot.RecipientExists, snapshot.RecipientAllowed, snapshot.RecipientCodeHash,
            snapshot.MaxPerTransaction, snapshot.DailyLimit, snapshot.AuthorizedToday,
            snapshot.ExecutedToday, snapshot.ExecutingToday, snapshot.AvailableBalance,
            snapshot.BlockHash, snapshot.ObservedAtUtc });
    }
    [HttpPost("assess")]
    public async Task<IActionResult> Assess(GatewayPaymentRequest request,
        [FromHeader(Name="X-Sentinel-Agent-Run")] Guid? runId, CancellationToken ct)
    {
        AuditEvent? run = null;
        if (runId is not null)
        {
            var owner = (await contract.GetConfigAsync(ct)).Owner;
            run = await db.AuditEvents.AsNoTracking().SingleOrDefaultAsync(x => x.Id == runId && x.ActorId == owner && x.EventType == "AgentRunStarted", ct)
                ?? throw new GatewayRejectedException("AGENT_RUN_INVALID");
            var constraints = JsonSerializer.Deserialize<JsonElement>(run.DetailsJson!);
            request.Validate(clock.GetUtcNow());
            if (request.Recipient != constraints.GetProperty("recipient").GetString() ||
                System.Numerics.BigInteger.Parse(request.AmountYocto) > System.Numerics.BigInteger.Parse(constraints.GetProperty("maxAmountYocto").GetString()!))
                throw new GatewayRejectedException("TASK_SPENDING_CONSTRAINT");
            if (await db.AuditEvents.AnyAsync(x => x.Id == AgentRunBinding.Id(runId.Value), ct))
                throw new GatewayRejectedException("AGENT_ASSESSMENT_ALREADY_BOUND");
        }
        var assessment = await gateway.AssessAsync(Actor, request, ct: ct);
        if (run is not null)
        {
            db.AuditEvents.Add(new AuditEvent { Id = AgentRunBinding.Id(runId!.Value), EventType = "AgentAssessmentBound", ActorId = run.ActorId,
                DetailsJson = JsonSerializer.Serialize(new { runId, assessmentId = assessment.AssessmentId }) });
            await db.SaveChangesAsync(ct);
        }
        return Ok(assessment);
    }
    [HttpGet("assessments/{id:guid}")]
    public async Task<IActionResult> Get(Guid id, CancellationToken ct) => Ok(await gateway.GetAsync(Actor, id, ct));
    [HttpPost("payments/{id}/assess-execution")]
    public async Task<IActionResult> AssessExecution(string id, CancellationToken ct)
    {
        var config = await contract.GetConfigAsync(ct);
        if (Actor != config.Owner && Actor != config.Agent) throw new UnauthorizedAccessException();
        var payment = await tracking.GetAsync(id, ct) ?? throw new KeyNotFoundException();
        var record = await db.PaymentRequests.AsNoTracking().SingleAsync(x => x.PaymentId == payment.PaymentId, ct);
        var request = new GatewayPaymentRequest(record.Recipient, record.AmountYocto, record.ExpiresAtNs.ToString(System.Globalization.CultureInfo.InvariantCulture), record.Purpose);
        return Ok(await gateway.AssessAsync(Actor, request, "Execution", id, ct));
    }
    [HttpPost("assessments/{id:guid}/approve")]
    [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
    public async Task<IActionResult> Approve(Guid id, ApproveAssessmentRequest request, CancellationToken ct)
    {
        await RequireHumanOwner(ct);
        return Ok(await gateway.ApproveAsync(Actor, id, request, ct));
    }
    [HttpPost("assessments/{id:guid}/prepare")]
    [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
    public async Task<IActionResult> Prepare(Guid id, PrepareAssessmentRequest request,
        [FromHeader(Name = "Idempotency-Key")] string? key, CancellationToken ct)
    {
        await RequireHumanOwner(ct);
        if (!Guid.TryParse(key, out _)) throw new ArgumentException("A GUID Idempotency-Key is required.");
        var assessment = await gateway.GetAsync(Actor, id, ct);
        return Ok(await tracking.PrepareAsync(Actor, key!, assessment.Intent.ToLegacy(), ct, id, request.ApprovalId));
    }
    [HttpPost("blocklist")]
    [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
    public async Task<IActionResult> AddBlocklist(AddBlocklistRequest request, CancellationToken ct)
    {
        await RequireHumanOwner(ct);
        if (!GatewayPaymentRequest.ValidAccount(request.AccountId) || !request.Confirmed ||
            request.ReasonCode is not ("CONFIRMED_SCAM" or "COMPROMISED_ACCOUNT" or "MALICIOUS_CONTRACT" or "MANUAL_SECURITY_HOLD") ||
            !Uri.TryCreate(request.EvidenceUri, UriKind.Absolute, out var source) || source.Scheme != "https" || source.UserInfo.Length > 0 ||
            request.EvidenceUri.Length > 1024 || source.Query.Length > 0 || source.Fragment.Length > 0 ||
            request.EvidenceSha256 is null || !Regex.IsMatch(request.EvidenceSha256, "^[a-f0-9]{64}$") ||
            (request.ExpiresAtUtc is not null && (request.ExpiresAtUtc <= clock.GetUtcNow() || request.ExpiresAtUtc > clock.GetUtcNow().AddDays(90))))
            throw new ArgumentException("Evidence-backed blocklist entry is invalid.");
        var entry = new CounterpartyBlocklistEntry { AccountId = request.AccountId, ReasonCode = request.ReasonCode,
            EvidenceUri = request.EvidenceUri, EvidenceSha256 = request.EvidenceSha256, RecordedBy = Actor,
            CreatedAtUtc = clock.GetUtcNow().UtcDateTime, ExpiresAtUtc = request.ExpiresAtUtc?.UtcDateTime };
        db.CounterpartyBlocklist.Add(entry);
        db.AuditEvents.Add(new() { EventType = "CounterpartyBlocklisted", ActorId = Actor,
            DetailsJson = JsonSerializer.Serialize(new { entry.Id, entry.AccountId, entry.ReasonCode, entry.EvidenceUri, entry.EvidenceSha256 }) });
        await db.SaveChangesAsync(ct); return Ok(new { entry.Id, entry.AccountId, entry.ReasonCode });
    }
    [HttpPost("blocklist/{id:guid}/revoke")]
    [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
    public async Task<IActionResult> Revoke(Guid id, CancellationToken ct)
    {
        await RequireHumanOwner(ct);
        var entry = await db.CounterpartyBlocklist.SingleOrDefaultAsync(x => x.Id == id, ct) ?? throw new KeyNotFoundException();
        entry.RevokedAtUtc ??= clock.GetUtcNow().UtcDateTime;
        db.AuditEvents.Add(new() { EventType = "CounterpartyBlocklistRevoked", ActorId = Actor, DetailsJson = JsonSerializer.Serialize(new { id }) });
        await db.SaveChangesAsync(ct); return Ok(new { entry.Id, entry.RevokedAtUtc });
    }
    private async Task RequireHumanOwner(CancellationToken ct)
    {
        // Authentication scheme restrictions on an action are combined with the
        // controller's schemes, so check the actual JWT identity as well.
        if (!User.Identities.Any(x => x.IsAuthenticated && x.AuthenticationType != GatewayAgentHandler.SchemeName &&
            x.HasClaim("sub", Actor)) || Actor != (await contract.GetConfigAsync(ct)).Owner)
            throw new UnauthorizedAccessException();
    }
}
