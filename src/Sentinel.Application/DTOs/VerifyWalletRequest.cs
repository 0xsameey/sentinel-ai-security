
namespace Sentinel.Application.DTOs;

public sealed record VerifyWalletRequest(
    Guid ChallengeId,
    string AccountId,
    string PublicKey,
    string SignatureBase64
);

public sealed record AuthTokenResponse(
    string AccessToken,
    string TokenType,
    int ExpiresIn,
    string AccountId
);
