
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Sentinel.Application.DTOs;
using Sentinel.Application.Interfaces;

namespace Sentinel.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/v1/policies")]
public sealed class PoliciesController : ControllerBase
{
    private readonly ISpendingPolicyService _policyService;
    private readonly IPolicyTransactionService _transactions;

    public PoliciesController(
        ISpendingPolicyService policyService,
        IPolicyTransactionService transactions)
    {
        _policyService = policyService;
        _transactions = transactions;
    }
    [HttpGet("current")]
    public async Task<IActionResult> GetCurrent(
        CancellationToken cancellationToken)
    {
        var policy = await _policyService
            .GetPolicyAsync(cancellationToken);

        return Ok(policy);
    }

    [HttpGet("usage")]
    public async Task<IActionResult> GetUsage(
        CancellationToken cancellationToken)
    {
        var usage = await _policyService
            .GetDailyUsageAsync(cancellationToken);

        return Ok(usage);
    }

    [HttpPut("current")]
    public async Task<IActionResult> UpdatePolicy(
        [FromBody] UpdateSpendingPolicyRequest request,
        CancellationToken cancellationToken)
    {
        var accountId = User.FindFirst("sub")?.Value;

        if (string.IsNullOrWhiteSpace(accountId))
            return Unauthorized();

        try
        {
            var transaction = await _transactions
                .PreparePolicyUpdateAsync(
                    accountId, request, cancellationToken);

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
    }

    [HttpPut("recipients/{accountId}")]
    public async Task<IActionResult> UpdateRecipient(
        string accountId,
        [FromBody] UpdateRecipientRequest request,
        CancellationToken cancellationToken)
    {
        var owner = User.FindFirst("sub")?.Value;

        if (string.IsNullOrWhiteSpace(owner))
            return Unauthorized();

        try
        {
            var transaction = await _transactions
                .PrepareRecipientUpdateAsync(
                    owner,
                    accountId,
                    request.Allowed,
                    cancellationToken);

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
    }


    [HttpGet("recipients/{accountId}")]
    public async Task<IActionResult> GetRecipientStatus(
        string accountId,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(accountId) ||
            accountId.Length > 64)
        {
            return BadRequest(new
            {
                message = "Invalid NEAR account ID."
            });
        }

        var recipient = await _policyService
            .GetRecipientStatusAsync(
                accountId,
                cancellationToken);

        return Ok(recipient);
    }
}
