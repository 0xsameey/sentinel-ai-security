
using Sentinel.Application.DTOs;

namespace Sentinel.Application.Interfaces;

public interface IPolicyTransactionService
{
    Task<ContractCallDto> PreparePolicyUpdateAsync(
        string accountId,
        UpdateSpendingPolicyRequest request,
        CancellationToken cancellationToken = default);

    Task<ContractCallDto> PrepareRecipientUpdateAsync(
        string accountId,
        string recipient,
        bool allowed,
        CancellationToken cancellationToken = default);
}
