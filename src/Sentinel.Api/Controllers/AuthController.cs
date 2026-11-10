
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Sentinel.Api.Authentication;
using Sentinel.Application.DTOs;
using Sentinel.Application.Interfaces;
using System.IdentityModel.Tokens.Jwt;

namespace Sentinel.Api.Controllers;

[ApiController]
[Route("api/v1/auth")]
public sealed class AuthController : ControllerBase
{
    private readonly IAuthChallengeService _challenges;
    private readonly IWalletVerificationService _verification;
    private readonly JwtTokenService _tokens;

    public AuthController(
        IAuthChallengeService challenges,
        IWalletVerificationService verification,
        JwtTokenService tokens)
    {
        _challenges = challenges;
        _verification = verification;
        _tokens = tokens;
    }

    [AllowAnonymous]
    [EnableRateLimiting("auth")]
    [HttpPost("challenge")]
    public async Task<IActionResult> CreateChallenge(
        [FromBody] ChallengeRequest request,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.AccountId) ||
            request.AccountId.Length > 64)
        {
            return BadRequest(new
            {
                message = "Invalid NEAR account ID."
            });
        }

        var challenge = await _challenges.CreateAsync(
            request.AccountId,
            cancellationToken);

        return Ok(challenge);
    }

    [AllowAnonymous]
    [HttpPost("verify")]
    public async Task<IActionResult> Verify(
        [FromBody] VerifyWalletRequest request,
        CancellationToken cancellationToken)
    {
        var accountId = await _verification.VerifyAsync(
            request,
            cancellationToken);

        if (accountId is null)
        {
            return Unauthorized(new
            {
                message = "Invalid or expired wallet signature."
            });
        }

        var (token, expiresIn) =
            _tokens.CreateToken(accountId);

        return Ok(new AuthTokenResponse(
            token,
            "Bearer",
            expiresIn,
            accountId));
    }

    [Authorize]
    [HttpGet("me")]
    public IActionResult Me()
    {
        var accountId = User.FindFirst(
            JwtRegisteredClaimNames.Sub)?.Value;

        return Ok(new
        {
            accountId,
            authenticated = true
        });
    }
}

public sealed record ChallengeRequest(string AccountId);
