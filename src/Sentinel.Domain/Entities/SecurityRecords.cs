namespace Sentinel.Domain.Entities;

public sealed class SecurityAssessmentRecord
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string OwnerId { get; set; } = "";
    public string ActorId { get; set; } = "";
    public string ContractId { get; set; } = "";
    public string? PaymentId { get; set; }
    public string Stage { get; set; } = "Authorization";
    public string IntentHash { get; set; } = "";
    public string IntentJson { get; set; } = "{}";
    public string DecisionJson { get; set; } = "{}";
    public string Fingerprint { get; set; } = "";
    public string Decision { get; set; } = "BLOCK";
    public DateTime CreatedAtUtc { get; set; }
    public DateTime ExpiresAtUtc { get; set; }
}
public sealed class HumanApprovalRecord
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid AssessmentId { get; set; }
    public string OwnerId { get; set; } = "";
    public string IntentHash { get; set; } = "";
    public string Fingerprint { get; set; } = "";
    public string? PaymentId { get; set; }
    public string Stage { get; set; } = "";
    public string? ConsumedByPaymentId { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public DateTime ExpiresAtUtc { get; set; }
    public DateTime? ConsumedAtUtc { get; set; }
}
public sealed class CounterpartyBlocklistEntry
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string AccountId { get; set; } = "";
    public string ReasonCode { get; set; } = "";
    public string EvidenceUri { get; set; } = "";
    public string EvidenceSha256 { get; set; } = "";
    public string RecordedBy { get; set; } = "";
    public DateTime CreatedAtUtc { get; set; }
    public DateTime? ExpiresAtUtc { get; set; }
    public DateTime? RevokedAtUtc { get; set; }
}
// One permanent claim per payment after consumption. Ambiguous submissions need
// reconciliation, never an automatic second signing attempt after a timeout.
public sealed class ExecutionPermitRecord
{
    public string PaymentId { get; set; } = "";
    public Guid PermitId { get; set; } = Guid.NewGuid();
    public Guid AssessmentId { get; set; }
    public Guid? ApprovalId { get; set; }
    public string IntentHash { get; set; } = "";
    public string Fingerprint { get; set; } = "";
    public string SignerPublicKey { get; set; } = "";
    public DateTime ExpiresAtUtc { get; set; }
    public DateTime? ConsumedAtUtc { get; set; }
}
