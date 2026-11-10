
using System.Net.Http.Json;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Sentinel.Infrastructure.Persistence;
using Sentinel.Application.Security;

namespace Sentinel.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/v1/payments")]
public sealed class AgentDispatchController : ControllerBase
{
    private readonly SentinelDbContext _db;
    private readonly IHttpClientFactory _httpFactory;
    private readonly IConfiguration _configuration;
    private readonly ISecurityGateway _gateway;

    public AgentDispatchController(
        SentinelDbContext db,
        IHttpClientFactory httpFactory,
        IConfiguration configuration, ISecurityGateway gateway)
    {
        _db = db;
        _httpFactory = httpFactory;
        _configuration = configuration;
        _gateway = gateway;
    }

    [HttpPost("{paymentId}/dispatch")]
    public async Task<IActionResult> Dispatch(
        string paymentId,
        CancellationToken cancellationToken)
    {
        var ownerId =
                User.FindFirst("account_id")?.Value ??
                User.FindFirst("accountId")?.Value ??
                User.FindFirst(ClaimTypes.NameIdentifier)?.Value ??
                User.FindFirst("sub")?.Value;

        if (string.IsNullOrWhiteSpace(ownerId))
            return Forbid();

        var record = await _db.PaymentRequests
            .AsNoTracking()
            .SingleOrDefaultAsync(
                x => x.PaymentId == paymentId &&
                     x.OwnerAccountId == ownerId,
                cancellationToken);

        if (record is null)
            return NotFound();

        if (record.RiskDecision is not ("ALLOW" or "WARN"))
            return Conflict(new
            {
                message = "Payment did not pass risk assessment."
            });

        if (record.Status != "Authorized")
            return Conflict(new
            {
                message = "Payment must be Authorized before execution.",
                record.Status
            });

        var configuredContract =
            _configuration["Near:ContractId"];

        if (!string.Equals(
                record.ContractId,
                configuredContract,
                StringComparison.Ordinal))
        {
            return Conflict(new
            {
                message = "Payment belongs to another contract."
            });
        }

        var dispatchUrl =
            _configuration["AgentDispatcher:Url"];

        await _gateway.PreflightExecutionAsync(ownerId, paymentId, cancellationToken);

        var dispatchKey =
            _configuration["AgentDispatcher:Key"];

        if (string.IsNullOrWhiteSpace(dispatchUrl) ||
            string.IsNullOrWhiteSpace(dispatchKey))
        {
            return Problem(
                "Agent dispatcher is not configured.",
                statusCode: 503);
        }

        // The URL must be a trusted server-side configuration,
        // never supplied by the caller.
        var http = _httpFactory.CreateClient(
            "SentinelAgentDispatcher");

        using var message = new HttpRequestMessage(
            HttpMethod.Post,
            $"{dispatchUrl.TrimEnd('/')}/execute");

        message.Headers.Add(
            "X-Sentinel-Dispatch-Key",
            dispatchKey);

        message.Content = JsonContent.Create(
            new { paymentId });

        try
        {
            using var response = await http.SendAsync(
                message,
                cancellationToken);

            var body = await response.Content
                .ReadAsStringAsync(cancellationToken);

            return StatusCode(
                (int)response.StatusCode,
                new
                {
                    message = response.IsSuccessStatusCode
                        ? "Execution request accepted."
                        : "Agent execution request failed.",
                    result = body
                });
        }
        catch (HttpRequestException)
        {
            return Problem(
                "Agent dispatcher is unavailable.",
                statusCode: 503);
        }
        catch (TaskCanceledException)
            when (!cancellationToken.IsCancellationRequested)
        {
            return Problem(
                "Agent dispatch timed out. Check payment status.",
                statusCode: 504);
        }
    }
}
