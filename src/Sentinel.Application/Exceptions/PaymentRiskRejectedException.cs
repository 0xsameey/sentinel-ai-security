
using Sentinel.Application.DTOs;

namespace Sentinel.Application.Exceptions;

public sealed class PaymentRiskRejectedException : Exception
{
    public RiskAssessmentDto Assessment { get; }

    public PaymentRiskRejectedException(
        RiskAssessmentDto assessment)
        : base("Payment did not pass risk assessment.")
    {
        Assessment = assessment;
    }
}
