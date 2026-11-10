
using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Sentinel.Application.DTOs;
using Sentinel.Application.Interfaces;
using Sentinel.Domain.Entities;
using Sentinel.Infrastructure.Persistence;

namespace Sentinel.Infrastructure.Authentication;

public sealed class AuthChallengeService : IAuthChallengeService
{
    private readonly SentinelDbContext _db;
    private readonly string _recipient;

    public AuthChallengeService(
        SentinelDbContext db,
        IConfiguration configuration)
    {
        _db = db;

        _recipient = configuration["Auth:Recipient"]
            ?? throw new InvalidOperationException(
                "Auth:Recipient is not configured.");

        if (string.IsNullOrWhiteSpace(_recipient))
            throw new InvalidOperationException(
                "Auth:Recipient cannot be empty.");
    }

    public async Task<AuthChallengeResponse> CreateAsync(
        string accountId,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(accountId) ||
            accountId.Length > 64)
        {
            throw new ArgumentException(
                "Invalid NEAR account ID.",
                nameof(accountId));
        }

        accountId = accountId.Trim();

        var now = DateTime.UtcNow;

        var nonceBytes = RandomNumberGenerator.GetBytes(32);
        var nonce = Convert.ToBase64String(nonceBytes);

        var challenge = new AuthChallenge
        {
            AccountId = accountId,
            NonceBase64 = nonce,
            Recipient = _recipient,
            Message = $"Sign in to Sentinel as {accountId}",
            CreatedAtUtc = now,
            ExpiresAtUtc = now.AddMinutes(5)
        };

        _db.AuthChallenges.Add(challenge);

        await _db.SaveChangesAsync(cancellationToken);

        return new AuthChallengeResponse(
            challenge.Id,
            challenge.Message,
            challenge.Recipient,
            challenge.NonceBase64,
            challenge.ExpiresAtUtc);
    }
}
