
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Sentinel.Application.DTOs;
using Sentinel.Application.Interfaces;
using Sentinel.Application.Exceptions;

namespace Sentinel.Api.Controllers;

[ApiController]
[Route("api/v1/payments")]
public sealed class PaymentsController : ControllerBase
{
    private readonly INearContractService _contractService;
    private readonly IPaymentTransactionService _transactions;

    private readonly IPaymentTrackingService _tracking;


    public PaymentsController(
        INearContractService contractService,
        IPaymentTransactionService transactions,
        IPaymentTrackingService tracking)
    {
        _contractService = contractService;
        _transactions = transactions;
        _tracking = tracking;
    }


    [HttpGet("{id}")]
    public async Task<IActionResult> GetPayment(
        string id,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(id) || id.Length > 64)
            return BadRequest(new
            {
                message = "Invalid payment ID."
            });

        var payment = await _contractService
            .GetPaymentAsync(id, cancellationToken);

        return payment is null
            ? NotFound(new { message = "Payment not found." })
            : Ok(payment);
    }


    [Authorize]
    [HttpPost("prepare")]
    public async Task<IActionResult> PreparePayment(
        [FromBody] AuthorizePaymentRequest request,
        [FromHeader(Name = "Idempotency-Key")] string? key,
        CancellationToken cancellationToken)
    {
        var owner = User.FindFirst("sub")?.Value;

        if (string.IsNullOrWhiteSpace(owner))
            return Unauthorized();

        if (string.IsNullOrWhiteSpace(key))
            return BadRequest(new
            {
                message = "Idempotency-Key header is required."
            });

        try
        {
            var result = await _tracking.PrepareAsync(
                owner, key, request, cancellationToken);

            return Ok(result);
        }

        catch (PaymentRiskRejectedException ex)
        {
            return UnprocessableEntity(new
            {
                message = "Payment did not pass security assessment.",
                ex.Assessment.RiskScore,
                ex.Assessment.RiskLevel,
                ex.Assessment.Decision,
                ex.Assessment.Reasons,
                ex.Assessment.AiExplanation
            });
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

    [Authorize]
    [HttpGet("{id}/tracking")]
    public async Task<IActionResult> GetTracking(
        string id,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(id) || id.Length > 64)
            return BadRequest();

        var accountId = User.FindFirst("sub")?.Value;
        var config = await _contractService
            .GetConfigAsync(cancellationToken);

        if (accountId != config.Owner &&
            accountId != config.Agent)
            return Forbid();

        var result = await _tracking.GetAsync(
            id, cancellationToken);

        return result is null ? NotFound() : Ok(result);
    }

    [Authorize]
    [HttpPost("{id}/sync")]
    public async Task<IActionResult> SyncPayment(
        string id,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(id) || id.Length > 64)
            return BadRequest();

        var accountId = User.FindFirst("sub")?.Value;
        var config = await _contractService
            .GetConfigAsync(cancellationToken);

        if (accountId != config.Owner &&
            accountId != config.Agent)
            return Forbid();

        var result = await _tracking.SyncAsync(
            id, cancellationToken);

        return result is null ? NotFound() : Ok(result);
    }


    [Authorize]
    [HttpPost("{id}/prepare-execution")]
    public async Task<IActionResult> PrepareExecution(
        string id,
        CancellationToken cancellationToken)
    {
        var accountId = User.FindFirst("sub")?.Value;

        if (string.IsNullOrEmpty(accountId))
            return Unauthorized();

        try
        {
            var transaction = await _transactions
                .PrepareExecutionAsync(
                    accountId, id, cancellationToken);

            return Ok(transaction);
        }
        catch (UnauthorizedAccessException)
        {
            return Forbid();
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new { message = ex.Message });
        }
        catch (InvalidOperationException ex)
        {
            return Conflict(new { message = ex.Message });
        }
    }

    [Authorize]
    [HttpPost("{id}/transactions")]
    public async Task<IActionResult> ReportTransaction(
        string id,
        [FromBody] ReportTransactionRequest request,
        CancellationToken cancellationToken)
    {
        var accountId = User.FindFirst("sub")?.Value;

        if (string.IsNullOrWhiteSpace(accountId))
            return Unauthorized();

        try
        {
            var result = await _tracking.ReportTransactionAsync(
                id,
                accountId,
                request,
                cancellationToken);

            return Ok(result);
        }
        catch (UnauthorizedAccessException)
        {
            return Forbid();
        }
        catch (KeyNotFoundException)
        {
            return NotFound();
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
