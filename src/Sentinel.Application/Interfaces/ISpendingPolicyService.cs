
using Sentinel.Application.DTOs;

namespace Sentinel.Application.Interfaces;

public interface ISpendingPolicyService
{
    Task<SpendingPolicyDto> GetPolicyAsync(
        CancellationToken cancellationToken = default);

    Task<DailyUsageDto> GetDailyUsageAsync(
        CancellationToken cancellationToken = default);

    Task<RecipientStatusDto> GetRecipientStatusAsync(
        string accountId,
        CancellationToken cancellationToken = default);
}
