
using Sentinel.Application.DTOs;

namespace Sentinel.Application.Interfaces;

public interface IRiskAssessmentService
{
    Task<RiskAssessmentDto> AssessAsync(
        AssessTransactionRequest request,
        CancellationToken cancellationToken = default);
}
