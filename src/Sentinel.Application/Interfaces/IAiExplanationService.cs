
using Sentinel.Application.DTOs;

namespace Sentinel.Application.Interfaces;

public interface IAiExplanationService
{
    Task<string?> ExplainAsync(
        AssessTransactionRequest request,
        string decision,
        IReadOnlyList<string> reasons,
        CancellationToken cancellationToken = default);
}
