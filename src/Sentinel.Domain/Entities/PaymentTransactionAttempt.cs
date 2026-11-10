
namespace Sentinel.Domain.Entities;

public sealed class PaymentTransactionAttempt
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid PaymentRequestId { get; set; }

    public PaymentRequestRecord PaymentRequest { get; set; } =
        null!;

    // Authorization or Execution
    public string Operation { get; set; } = string.Empty;

    public string SignerAccountId { get; set; } = string.Empty;

    public string? TransactionHash { get; set; }

    // Reported, Verified, Failed, Unknown
    public string Status { get; set; } = "Reported";

    public DateTime CreatedAtUtc { get; set; } =
        DateTime.UtcNow;

    public DateTime? VerifiedAtUtc { get; set; }
}
