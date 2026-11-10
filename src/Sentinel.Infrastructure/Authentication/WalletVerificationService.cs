
using Microsoft.EntityFrameworkCore;
using Sentinel.Application.DTOs;
using Sentinel.Application.Interfaces;
using Sentinel.Infrastructure.Near;
using Sentinel.Infrastructure.Persistence;

namespace Sentinel.Infrastructure.Authentication;

public sealed class WalletVerificationService
    : IWalletVerificationService
{
    private readonly SentinelDbContext _db;
    private readonly Nep413Verifier _verifier;
    private readonly NearAccessKeyService _keys;

    public WalletVerificationService(
        SentinelDbContext db,
        Nep413Verifier verifier,
        NearAccessKeyService keys)
    {
        _db = db;
        _verifier = verifier;
        _keys = keys;
    }

    public async Task<string?> VerifyAsync(
        VerifyWalletRequest request,
        CancellationToken cancellationToken = default)
    {
        if (request.ChallengeId == Guid.Empty ||
            string.IsNullOrWhiteSpace(request.AccountId) ||
            request.AccountId.Length > 64 ||
            string.IsNullOrWhiteSpace(request.PublicKey) ||
            request.PublicKey.Length > 100 ||
            string.IsNullOrWhiteSpace(request.SignatureBase64) ||
            request.SignatureBase64.Length > 128)
            return null;

        var challenge = await _db.AuthChallenges
            .AsNoTracking()
            .SingleOrDefaultAsync(
                x => x.Id == request.ChallengeId,
                cancellationToken);

        if (challenge is null ||
            challenge.ConsumedAtUtc is not null ||
            challenge.ExpiresAtUtc <= DateTime.UtcNow ||
            !string.Equals(
                challenge.AccountId,
                request.AccountId,
                StringComparison.Ordinal))
            return null;

        var signatureValid = _verifier.Verify(
            challenge.Message,
            challenge.Recipient,
            challenge.NonceBase64,
            request.PublicKey,
            request.SignatureBase64);

        if (!signatureValid)
            return null;

        var keyAuthorized = await _keys.IsAuthorizedAsync(
            challenge.AccountId,
            request.PublicKey,
            cancellationToken);

        if (!keyAuthorized)
            return null;

        // Atomic conditional UPDATE prevents two concurrent
        // requests from consuming the same challenge.
        var consumed = await _db.AuthChallenges
            .Where(x =>
                x.Id == challenge.Id &&
                x.ConsumedAtUtc == null &&
                x.ExpiresAtUtc > DateTime.UtcNow)
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(
                        x => x.ConsumedAtUtc,
                        DateTime.UtcNow),
                cancellationToken);

        return consumed == 1
            ? challenge.AccountId
            : null;
    }
}
