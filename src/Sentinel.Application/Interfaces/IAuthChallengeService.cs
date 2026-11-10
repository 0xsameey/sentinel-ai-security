
using Sentinel.Application.DTOs;

namespace Sentinel.Application.Interfaces;

public interface IAuthChallengeService
{
    Task<AuthChallengeResponse> CreateAsync(
        string accountId,
        CancellationToken cancellationToken = default);
}
