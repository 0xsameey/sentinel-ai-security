
namespace Sentinel.Domain.Entities;

public sealed class PaymentRequestRecord
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public string PaymentId { get; set; } = string.Empty;

    public string IdempotencyKey { get; set; } = string.Empty;

    public string OwnerAccountId { get; set; } = string.Empty;

    public string ContractId { get; set; } = string.Empty;

    public string Recipient { get; set; } = string.Empty;

    public string AmountYocto { get; set; } = string.Empty;

    public ulong ExpiresAtNs { get; set; }

    public string Status { get; set; } = "Prepared";

    public DateTime CreatedAtUtc { get; set; } =
        DateTime.UtcNow;

    public DateTime UpdatedAtUtc { get; set; } =
        DateTime.UtcNow;

    public DateTime? CompletedAtUtc { get; set; }


    public string Purpose { get; set; } = string.Empty;

    public int RiskScore { get; set; }

    public string RiskLevel { get; set; } = string.Empty;

    public string RiskDecision { get; set; } = string.Empty;

    public string RiskReasonsJson { get; set; } = "[]";

    public string? AiExplanation { get; set; }
    public Guid? SecurityAssessmentId { get; set; }
    public Guid? SecurityApprovalId { get; set; }
    public string? SecurityIntentHash { get; set; }

}
