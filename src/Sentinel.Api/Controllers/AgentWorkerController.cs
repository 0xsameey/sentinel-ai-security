
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Sentinel.Api.Authentication;
using Sentinel.Application.DTOs;
using Sentinel.Application.Interfaces;
using Sentinel.Application.Security;
using Microsoft.AspNetCore.RateLimiting;

namespace Sentinel.Api.Controllers;

[ApiController]
[AllowAnonymous]
[Route("api/internal/agent")]
[EnableRateLimiting("worker")]
public sealed class AgentWorkerController : ControllerBase
{
    private readonly AgentWorkerAuthenticator _authenticator;
    private readonly IPaymentTransactionService _transactions;
    private readonly INearContractService _contract;
    private readonly IPaymentTrackingService _tracking;
    private readonly ISecurityGateway _gateway;

    public AgentWorkerController(
        AgentWorkerAuthenticator authenticator,
        IPaymentTransactionService transactions,
        INearContractService contract,
        IPaymentTrackingService tracking, ISecurityGateway gateway)
    {
        _authenticator = authenticator;
        _transactions = transactions;
        _contract = contract;
        _tracking = tracking;
        _gateway = gateway;
    }


    [HttpPost("payments/{id}/prepare-execution")]
    public async Task<IActionResult> PrepareExecution(
        string id,
        [FromHeader(Name = "X-Sentinel-Worker-Key")]
        string? workerKey,
        CancellationToken cancellationToken)
    {
        if (!_authenticator.IsValid(workerKey))
            return Unauthorized();

        if (string.IsNullOrWhiteSpace(id) ||
            id.Length > 64)
        {
            return BadRequest(new
            {
                message = "Invalid payment ID."
            });
        }

        // Resolve the current agent directly from the contract.
        var config = await _contract.GetConfigAsync(
            cancellationToken);

        if (config.Paused)
            return Conflict(new { message = "Contract paused." });

        try
        {
            var transaction = await _transactions
                .PrepareExecutionAsync(
                    config.Agent,
                    id,
                    cancellationToken);

            return Ok(transaction);
        }
        catch (KeyNotFoundException)
        {
            return NotFound();
        }
        catch (InvalidOperationException ex)
        {
            return Conflict(new { message = ex.Message });
        }


    }

    [HttpPost("payments/{id}/consume-permit")]
    public async Task<IActionResult> ConsumePermit(string id, ConsumePermitRequest request,
        [FromHeader(Name = "X-Sentinel-Worker-Key")] string? key, CancellationToken cancellationToken)
    {
        if (!_authenticator.IsValid(key)) return Unauthorized();
        return Ok(await _gateway.ConsumeAsync(id, request, cancellationToken));
    }

    [HttpPost("payments/{id}/report-transaction")]
    public async Task<IActionResult> ReportTransaction(
        string id,
        [FromBody] ReportTransactionRequest request,
        [FromHeader(Name = "X-Sentinel-Worker-Key")]
    string? workerKey,
        CancellationToken cancellationToken)
    {
        if (!_authenticator.IsValid(workerKey))
            return Unauthorized();

        if (string.IsNullOrWhiteSpace(id) ||
            id.Length > 64)
            return BadRequest(new
            {
                message = "Invalid payment ID."
            });

        // Internal agent workers can report execution only.
        if (request.Operation != "Execution")
            return BadRequest(new
            {
                message = "Unsupported worker operation."
            });

        try
        {
            var config = await _contract.GetConfigAsync(
                cancellationToken);

            var result = await _tracking.ReportTransactionAsync(
                id,
                config.Agent,
                request,
                cancellationToken);

            return Ok(result);
        }
        catch (KeyNotFoundException)
        {
            return NotFound();
        }
        catch (UnauthorizedAccessException)
        {
            return Forbid();
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
        catch (InvalidOperationException ex)
        {
            return Conflict(new { message = ex.Message });
        }
    }

}
