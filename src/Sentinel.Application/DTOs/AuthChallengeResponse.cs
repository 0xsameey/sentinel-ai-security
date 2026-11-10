
namespace Sentinel.Application.DTOs;

public sealed record AuthChallengeResponse(
    Guid ChallengeId,
    string Message,
    string Recipient,
    string NonceBase64,
    DateTime ExpiresAtUtc
);
