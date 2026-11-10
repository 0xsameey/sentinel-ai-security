using System.Globalization;
using System.Numerics;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Sentinel.Application.Interfaces;
using Sentinel.Application.Security;
using Sentinel.Domain.Entities;
using Sentinel.Infrastructure.Persistence;

namespace Sentinel.Api.Controllers;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record AgentTaskRequest(string Task, string Recipient, string MaxAmountYocto);

[ApiController, Authorize, Route("api/v1/agent/tasks"), EnableRateLimiting("gateway"), RequestSizeLimit(8192)]
public sealed class AgentTasksController(INearContractService contract, ISecurityFactsProvider facts,
    ISecurityGateway gateway, SentinelDbContext db, IHttpClientFactory clients, IConfiguration config,
    TimeProvider clock) : ControllerBase
{
    private async Task<string> Owner(CancellationToken ct)
    {
        var owner = (await contract.GetConfigAsync(ct)).Owner;
        if (User.FindFirst("sub")?.Value != owner) throw new UnauthorizedAccessException();
        return owner;
    }
    [HttpPost]
    public async Task<IActionResult> Run(AgentTaskRequest request, [FromHeader(Name="Idempotency-Key")] string? key, CancellationToken ct)
    {
        var owner = await Owner(ct);
        if (!Guid.TryParse(key, out var runId) || runId == Guid.Empty || string.IsNullOrWhiteSpace(request.Task) || request.Task.Length > 2000)
            throw new ArgumentException("Invalid bounded agent task.");
        new GatewayPaymentRequest(request.Recipient, request.MaxAmountYocto,
            ((ulong)clock.GetUtcNow().AddMinutes(15).ToUnixTimeMilliseconds()*1_000_000UL).ToString(CultureInfo.InvariantCulture), "task ceiling").Validate(clock.GetUtcNow());
        if (await db.AuditEvents.AnyAsync(x => x.Id == runId, ct)) return Conflict(new { code = "AGENT_RUN_ALREADY_CLAIMED" });
        var endpoint = config["AgentRunner:Url"];
        var serviceKey = config["AgentRunner:ApiKey"];
        if (!Uri.TryCreate(endpoint, UriKind.Absolute, out var uri) || uri.UserInfo.Length > 0 || uri.Query.Length > 0 || uri.Fragment.Length > 0 ||
            !(uri.Scheme == "https" || uri.Scheme == "http" && uri.IsLoopback) || serviceKey is null || serviceKey.Length < 32)
            return StatusCode(503, new { code = "AGENT_RUNNER_UNCONFIGURED" });
        var taskHash = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(request.Task)));
        var claim = new AuditEvent { Id = runId, EventType = "AgentRunStarted", ActorId = owner,
            DetailsJson = JsonSerializer.Serialize(new { runId, recipient = request.Recipient, maxAmountYocto = request.MaxAmountYocto, taskHash }) };
        db.AuditEvents.Add(claim);
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateException ex) when (ex.InnerException is Npgsql.PostgresException pg && pg.SqlState == Npgsql.PostgresErrorCodes.UniqueViolation)
        { db.ChangeTracker.Clear(); return Conflict(new { code = "AGENT_RUN_ALREADY_CLAIMED" }); }
        try
        {
            var current = await facts.FetchAsync(request.Recipient, ct);
            if (!current.Available || !current.RecipientExists || !current.RecipientAllowed || current.Paused || request.Recipient == current.ContractId)
                throw new GatewayRejectedException("AGENT_RECIPIENT_OR_POLICY_UNAVAILABLE");
            using var outbound = new HttpRequestMessage(HttpMethod.Post, new Uri(uri, "tasks"));
            outbound.Headers.Add("X-Sentinel-Runner-Key", serviceKey);
            outbound.Content = JsonContent.Create(new { runId, task = request.Task, recipient = request.Recipient, maxAmountYocto = request.MaxAmountYocto });
            using var response = await clients.CreateClient("SentinelAgentRunner").SendAsync(outbound, ct);
            if (!response.IsSuccessStatusCode) throw new GatewayRejectedException("AGENT_RUN_UNAVAILABLE");
            var payload = await response.Content.ReadFromJsonAsync<JsonElement>(ct);
            if (payload.GetProperty("runId").GetGuid() != runId) throw new GatewayRejectedException("AGENT_RESPONSE_INVALID");
            var assessmentId = payload.GetProperty("assessmentId").GetGuid();
            var binding = await db.AuditEvents.AsNoTracking().SingleOrDefaultAsync(x => x.Id == AgentRunBinding.Id(runId) &&
                x.ActorId == owner && x.EventType == "AgentAssessmentBound", ct);
            if (binding is null || JsonSerializer.Deserialize<JsonElement>(binding.DetailsJson!).GetProperty("assessmentId").GetGuid() != assessmentId)
                throw new GatewayRejectedException("AGENT_RESPONSE_INVALID");
            var assessment = await gateway.GetAsync(owner, assessmentId, ct);
            if (assessment.Stage != "Authorization" || assessment.Intent.Recipient != request.Recipient ||
                BigInteger.Parse(assessment.Intent.AmountYocto) > BigInteger.Parse(request.MaxAmountYocto) ||
                assessment.Risk.ExpiresAtUtc <= clock.GetUtcNow()) throw new GatewayRejectedException("AGENT_RESPONSE_INVALID");
            assessment.Intent.Validate(clock.GetUtcNow());
            // Always reconstruct status from persisted gateway evidence, never model/runner prose.
            var status = assessment.Risk.Decision switch { "ALLOW" => "OWNER_AUTHORIZATION_REQUIRED", "WARN" => "HUMAN_APPROVAL_REQUIRED", _ => "BLOCKED" };
            claim.EventType = "AgentRunCompleted";
            claim.DetailsJson = JsonSerializer.Serialize(new { runId, status, assessment, recipient = request.Recipient,
                maxAmountYocto = request.MaxAmountYocto, taskHash });
            await db.SaveChangesAsync(ct);
            return Ok(new { runId, status, assessment });
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            claim.EventType = "AgentRunFailed";
            var code = ex is GatewayRejectedException rejected ? rejected.Code : "AGENT_RUN_UNAVAILABLE";
            claim.DetailsJson = JsonSerializer.Serialize(new { runId, code, recipient = request.Recipient,
                maxAmountYocto = request.MaxAmountYocto, taskHash });
            await db.SaveChangesAsync(ct);
            return StatusCode(422, new { runId, code });
        }
    }
    [HttpGet("{id:guid}")]
    public async Task<IActionResult> Get(Guid id, CancellationToken ct)
    {
        var owner = await Owner(ct);
        var run = await db.AuditEvents.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id && x.ActorId == owner &&
            (x.EventType == "AgentRunStarted" || x.EventType == "AgentRunCompleted" || x.EventType == "AgentRunFailed"), ct);
        return run is null ? NotFound() : Ok(new { id, state = run.EventType, run.CreatedAtUtc, details = JsonSerializer.Deserialize<JsonElement>(run.DetailsJson!) });
    }
}
