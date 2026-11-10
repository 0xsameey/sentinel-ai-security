using System.Text.Json.Serialization;
using Sentinel.Application.DTOs;

namespace Sentinel.Application.Security;

public sealed record DelegationState(string Agent, string MaxPayment, string Budget, string DailyLimit,
    string ExpiresAtNs, string[] Recipients, bool Revoked, string Spent, string Day, string SpentToday);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record CreateDelegationRequest(string Id, string Agent, string MaxPayment, string Budget,
    string DailyLimit, string ExpiresAtNs, string[] Recipients);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record DelegatedProposalRequest(string DelegationId, GatewayPaymentRequest Intent);
public interface IDelegationReader
{
    Task<string?> PaymentDelegationAsync(string id, string blockHash, CancellationToken ct);
    Task<DelegationState?> ReadAsync(string id, string blockHash, CancellationToken ct);
}
public interface IDelegatedReleaseGate
{
    Task<string> VerifyAsync(SecurityFacts facts, CancellationToken ct);
}
