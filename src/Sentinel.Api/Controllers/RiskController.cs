using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Sentinel.Application.DTOs;
using Sentinel.Application.Security;

namespace Sentinel.Api.Controllers;

[ApiController]
[Authorize]
[EnableRateLimiting("gateway")]
[Route("api/v1/risk")]
public sealed class RiskController(ISecurityGateway gateway, TimeProvider clock) : ControllerBase
{
    [HttpPost("assess")]
    public async Task<IActionResult> Assess(AssessTransactionRequest request, CancellationToken ct)
    {
        var actor = User.FindFirst("sub")?.Value ?? throw new UnauthorizedAccessException();
        var expiry = checked((ulong)clock.GetUtcNow().AddMinutes(15).ToUnixTimeMilliseconds() * 1_000_000UL);
        var assessment = await gateway.AssessAsync(actor,
            new(request.Recipient, request.AmountYocto, expiry.ToString(System.Globalization.CultureInfo.InvariantCulture), request.Purpose), ct: ct);
        return Ok(new { riskScore = assessment.Risk.Score, riskLevel = assessment.Risk.Decision == "BLOCK" ? "High" : assessment.Risk.Decision == "WARN" ? "Medium" : "Low",
            decision = assessment.Risk.Decision, reasons = assessment.Risk.ReasonCodes, aiExplanation = (string?)null,
            assessmentId = assessment.AssessmentId, assessment.Risk.Evidence, assessment.Risk.AssessedAtUtc, assessment.Risk.ExpiresAtUtc });
    }
}
