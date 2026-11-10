
namespace Sentinel.Domain.Entities;

public sealed class AuthChallenge
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public string AccountId { get; set; } = string.Empty;

    public string NonceBase64 { get; set; } = string.Empty;

    public string Message { get; set; } = string.Empty;

    public string Recipient { get; set; } = string.Empty;

    public DateTime CreatedAtUtc { get; set; }

    public DateTime ExpiresAtUtc { get; set; }

    public DateTime? ConsumedAtUtc { get; set; }
}
