
namespace Sentinel.Application.DTOs;

public sealed record AssessTransactionRequest(
    string Recipient,
    string AmountYocto,
    string Purpose
);

public sealed record RiskAssessmentDto(
    int RiskScore,
    string RiskLevel,
    string Decision,
    List<string> Reasons,
    string? AiExplanation
);
