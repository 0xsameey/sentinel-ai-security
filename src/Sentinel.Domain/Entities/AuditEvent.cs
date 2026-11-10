
namespace Sentinel.Domain.Entities;

public sealed class AuditEvent
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public string EventType { get; set; } = string.Empty;

    public string? ActorId { get; set; }

    public string? PaymentId { get; set; }

    public string? DetailsJson { get; set; }

    public DateTime CreatedAtUtc { get; set; } =
        DateTime.UtcNow;
}
