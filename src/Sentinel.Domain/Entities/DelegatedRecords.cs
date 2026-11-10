namespace Sentinel.Domain.Entities;

// Indexed chain state is informational. Every release reads finalized chain state again.
public sealed class DelegationRecord
{
    public string Id { get; set; } = "";
    public string ContractId { get; set; } = "";
    public string OwnerId { get; set; } = "";
    public string StateJson { get; set; } = "null";
    public DateTime ObservedAtUtc { get; set; }
}
public sealed class DelegatedPaymentRecord
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string DelegationId { get; set; } = "";
    public string ContractId { get; set; } = "";
    public string OwnerId { get; set; } = "";
    public string AgentId { get; set; } = "";
    public string IntentJson { get; set; } = "{}";
    public string IntentHash { get; set; } = "";
    public string DecisionJson { get; set; } = "{}";
    public string Decision { get; set; } = "BLOCK";
    public string Fingerprint { get; set; } = "";
    public DateTime? ApprovedAtUtc { get; set; }
    public DateTime? ApprovalExpiresAtUtc { get; set; }
    public string? ApprovalFingerprint { get; set; }
    public Guid? PermitId { get; set; }
    public string? PermitFingerprint { get; set; }
    public string? SignerPublicKey { get; set; }
    public DateTime? PermitExpiresAtUtc { get; set; }
    public DateTime? ConsumedAtUtc { get; set; }
    public string Status { get; set; } = "Proposed";
    public string? TransactionHash { get; set; }
    public DateTime CreatedAtUtc { get; set; }
}
