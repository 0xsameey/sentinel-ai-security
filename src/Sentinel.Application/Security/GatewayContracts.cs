using System.ComponentModel.DataAnnotations;
using System.Globalization;
using System.Numerics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Sentinel.Application.DTOs;

namespace Sentinel.Application.Security;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record GatewayPaymentRequest(string Recipient, string AmountYocto, string ExpiresAtNs, string Purpose)
{
    public GatewayPaymentRequest Validate(DateTimeOffset now)
    {
        if (!ValidAccount(Recipient) || string.IsNullOrWhiteSpace(Purpose) || Purpose.Length > 500 ||
            AmountYocto is null || !Regex.IsMatch(AmountYocto, "^[1-9][0-9]{0,38}$") ||
            BigInteger.Parse(AmountYocto, CultureInfo.InvariantCulture) > (BigInteger.One << 128) - 1 ||
            ExpiresAtNs is null || !Regex.IsMatch(ExpiresAtNs, "^[1-9][0-9]{0,19}$") ||
            !ulong.TryParse(ExpiresAtNs, out var expiry)) throw new ArgumentException("Invalid payment intent.");
        var time = checked((ulong)now.ToUnixTimeMilliseconds() * 1_000_000UL);
        if (expiry <= time) throw new PaymentExpiryException("PAYMENT_EXPIRED", "Payment expired. Create a fresh proposal; an existing intent cannot be extended.");
        if (expiry > time + 86_400_000_000_000UL) throw new PaymentExpiryException("PAYMENT_EXPIRY_EXCEEDS_24_HOURS", "Payment expiry exceeds the 24-hour maximum.");
        return this;
    }
    public static bool ValidAccount(string? account) => account is not null && account.Length is >= 2 and <= 64 &&
        Regex.IsMatch(account, "^[a-z0-9]+(?:[-_.][a-z0-9]+)*$");
    public AuthorizePaymentRequest ToLegacy() => new(Recipient, AmountYocto, ulong.Parse(ExpiresAtNs, CultureInfo.InvariantCulture), Purpose);
    public static GatewayPaymentRequest FromLegacy(AuthorizePaymentRequest request) => new(request.Recipient,
        BigInteger.Parse(request.AmountYocto, CultureInfo.InvariantCulture).ToString(CultureInfo.InvariantCulture),
        request.ExpiresAtNs.ToString(CultureInfo.InvariantCulture), request.Purpose);
    public string Hash(string contract, string owner) => HashText(JsonSerializer.Serialize(new { Network = "testnet", Contract = contract,
        Owner = owner, Method = "authorize_payment", Recipient, AmountYocto, ExpiresAtNs, Purpose }));
    public static string HashText(string value) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
}

public sealed class PaymentExpiryException(string code, string message) : ArgumentException(message)
{
    public string Code { get; } = code;
}

public sealed record RiskEvidence(string Code, string Source, DateTimeOffset ObservedAtUtc, string? BlockHash, string Detail);
public sealed record HistorySignal(bool Available, int SampleSize, int FailedCount, string Digest,
    DateTimeOffset ObservedAtUtc, string Source);
public sealed record SecurityFacts(bool Available, string ContractId, string Owner, string Agent, bool Paused,
    bool RecipientExists, string RecipientCodeHash, bool RecipientAllowed, string MaxPerTransaction, string DailyLimit,
    string AuthorizedToday, string ExecutedToday, string ExecutingToday, string DayIndex, string AvailableBalance,
    string ContractCodeHash, string BlockHash, DateTimeOffset ObservedAtUtc, HistorySignal History,
    IReadOnlyList<RiskEvidence> Evidence);
public sealed record RiskDecision(string Decision, int Score, IReadOnlyList<string> ReasonCodes,
    IReadOnlyList<RiskEvidence> Evidence, string Fingerprint, DateTimeOffset AssessedAtUtc, DateTimeOffset ExpiresAtUtc);
public sealed record GatewayAssessmentDto(Guid AssessmentId, string IntentHash, string Stage, string? PaymentId,
    GatewayPaymentRequest Intent, RiskDecision Risk);
public sealed record HumanApprovalDto(Guid ApprovalId, Guid AssessmentId, string Fingerprint, DateTimeOffset ExpiresAtUtc);

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record ApproveAssessmentRequest([Required] string Fingerprint, bool Confirm);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record PrepareAssessmentRequest(Guid? ApprovalId);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record ConsumePermitRequest(Guid PermitId, [Required] string IntentHash);
public sealed record ExecutionSecurityDto(Guid PermitId, string IntentHash, DateTimeOffset ExpiresAtUtc, string SignerPublicKey);
public sealed record ConsumedPermitDto(Guid PermitId, string PaymentId, string IntentHash, string Decision,
    bool HumanApproved, DateTimeOffset ExpiresAtUtc, string SignerPublicKey, ContractCallDto Transaction);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record AddBlocklistRequest(string AccountId, string ReasonCode, string EvidenceUri, string EvidenceSha256,
    bool Confirmed, DateTimeOffset? ExpiresAtUtc);

public interface ISecurityFactsProvider { Task<SecurityFacts> FetchAsync(string recipient, CancellationToken ct = default); }
public interface ICounterpartyHistory { Task<HistorySignal> FetchAsync(string recipient, CancellationToken ct = default); }
public interface IExecutionReleaseGate { Task<string> VerifyAsync(SecurityFacts facts, CancellationToken ct = default); }
public interface ISecurityGateway
{
    Task<GatewayAssessmentDto> AssessAsync(string actor, GatewayPaymentRequest request, string stage = "Authorization", string? paymentId = null, CancellationToken ct = default);
    Task<GatewayAssessmentDto> GetAsync(string actor, Guid assessmentId, CancellationToken ct = default);
    Task<HumanApprovalDto> ApproveAsync(string owner, Guid assessmentId, ApproveAssessmentRequest request, CancellationToken ct = default);
    Task<GatewayAssessmentDto> AuthorizePreparationAsync(string owner, GatewayPaymentRequest request, Guid? assessmentId,
        Guid? approvalId, string paymentId, CancellationToken ct = default);
    Task PreflightExecutionAsync(string actor, string paymentId, CancellationToken ct = default);
    Task<ContractCallDto> PrepareExecutionAsync(string agent, string paymentId, CancellationToken ct = default);
    Task<ConsumedPermitDto> ConsumeAsync(string paymentId, ConsumePermitRequest request, CancellationToken ct = default);
}

public sealed class GatewayRejectedException(string code, GatewayAssessmentDto? assessment = null) : Exception(code)
{
    public string Code { get; } = code;
    public GatewayAssessmentDto? Assessment { get; } = assessment;
}
