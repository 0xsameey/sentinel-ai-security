
using Sentinel.Application.DTOs;

namespace Sentinel.Application.Interfaces;

public interface IPaymentTransactionService
{
    Task<PreparedPaymentDto> PrepareAuthorizationAsync(
        string ownerAccountId,
        AuthorizePaymentRequest request,
        CancellationToken cancellationToken = default, string? paymentId = null);

    Task<ContractCallDto> PrepareExecutionAsync(
        string agentAccountId,
        string paymentId,
        CancellationToken cancellationToken = default);
}
