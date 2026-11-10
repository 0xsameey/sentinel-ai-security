using System.Numerics;
using System.Text.Json;

namespace Sentinel.Application.Security;

// Only structured facts participate. Purpose and explanations are never interpreted as instructions.
public sealed class DeterministicRiskEngine
{
    public const string Version = "sentinel-gateway-v2.0";
    public RiskDecision Evaluate(GatewayPaymentRequest request, SecurityFacts facts, string stage,
        IReadOnlyList<RiskEvidence> blocklist, DateTimeOffset now)
    {
        var reasons = new List<string>();
        var blocked = false;
        var warn = false;
        void Block(string code) { blocked = true; reasons.Add(code); }
        void Warn(string code) { warn = true; reasons.Add(code); }
        if (!facts.Available || now - facts.ObservedAtUtc > TimeSpan.FromSeconds(30) || facts.ObservedAtUtc > now.AddSeconds(5)) Block("CRITICAL_RPC_UNAVAILABLE");
        if (blocklist.Count > 0) Block("COUNTERPARTY_BLOCKLISTED");
        var amount = BigInteger.Parse(request.AmountYocto);
        if (facts.Available)
        {
            if (facts.Paused) Block("CONTRACT_PAUSED");
            if (!facts.RecipientExists) Block("RECIPIENT_NOT_FOUND");
            if (!facts.RecipientAllowed || request.Recipient == facts.ContractId) Block("RECIPIENT_NOT_ALLOWED");
            try
            {
                var max = Parse(facts.MaxPerTransaction); var daily = Parse(facts.DailyLimit);
                var authorized = Parse(facts.AuthorizedToday); var executed = Parse(facts.ExecutedToday); var executing = Parse(facts.ExecutingToday);
                if (max == 0 || daily == 0 || amount > max) Block("PER_TRANSACTION_LIMIT");
                if (stage == "Authorization" && authorized + amount > daily) Block("DAILY_AUTHORIZATION_QUOTA");
                if (executed + executing + amount > daily) Block("DAILY_EXECUTION_QUOTA");
                if (amount > Parse(facts.AvailableBalance)) Block("INSUFFICIENT_TREASURY");
                if (max > 0 && amount * 100 >= max * 80) Warn("NEAR_TRANSACTION_LIMIT");
            }
            catch (Exception ex) when (ex is FormatException or ArgumentException) { Block("MALFORMED_RPC_POLICY"); }
        }
        var historyFresh = facts.History.Available && now - facts.History.ObservedAtUtc <= TimeSpan.FromMinutes(2) && facts.History.ObservedAtUtc <= now.AddSeconds(5);
        if (!historyFresh) Warn("INDEXED_HISTORY_UNAVAILABLE");
        else if (facts.History.SampleSize == 0) Warn("NO_INDEXED_HISTORY");
        else if (facts.History.FailedCount >= 3 && facts.History.FailedCount * 2 >= facts.History.SampleSize) Warn("HIGH_RECENT_FAILURE_RATE");
        if (facts.RecipientCodeHash.Length > 0 && facts.RecipientCodeHash != "11111111111111111111111111111111") Warn("RECIPIENT_HAS_CONTRACT");
        if (reasons.Count == 0) reasons.Add("POLICY_AND_EVIDENCE_PASS");
        var decision = blocked ? "BLOCK" : warn ? "WARN" : "ALLOW";
        var fingerprint = GatewayPaymentRequest.HashText(JsonSerializer.Serialize(new {
            Version, Stage = stage, facts.ContractId, facts.Owner, facts.Agent, facts.Paused, facts.Available,
            facts.RecipientExists, facts.RecipientAllowed, facts.RecipientCodeHash, facts.ContractCodeHash,
            facts.MaxPerTransaction, facts.DailyLimit, facts.AuthorizedToday, facts.ExecutedToday, facts.ExecutingToday,
            facts.DayIndex, facts.AvailableBalance, HistoryAvailable = historyFresh, facts.History.Digest,
            Blocklist = blocklist.Select(x => x.Detail).Order().ToArray(), Decision = decision, Reasons = reasons.Order().ToArray()
        }));
        return new(decision, blocked ? 100 : warn ? 50 : 0, reasons,
            facts.Evidence.Concat(blocklist).ToArray(), fingerprint, now, now.AddMinutes(2));
    }
    private static BigInteger Parse(string value)
    {
        if (string.IsNullOrEmpty(value) || value.Length > 39 || value.Any(c => c < '0' || c > '9')) throw new FormatException();
        var n = BigInteger.Parse(value);
        if (n > (BigInteger.One << 128) - 1) throw new FormatException();
        return n;
    }
}
