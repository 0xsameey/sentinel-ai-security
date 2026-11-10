using System.Globalization;
using System.Numerics;

namespace Sentinel.Application.Security;

public static class DelegationConstraints
{
    public static string[] Failures(DelegationState d, string agent, GatewayPaymentRequest intent, DateTimeOffset now)
    {
        static BigInteger N(string s) => BigInteger.Parse(s, CultureInfo.InvariantCulture);
        var ns = new BigInteger(now.ToUnixTimeMilliseconds()) * 1_000_000;
        var amount = N(intent.AmountYocto);
        var dailySpent = N(d.Day) == ns / 86_400_000_000_000L ? N(d.SpentToday) : BigInteger.Zero;
        var failures = new List<string>();
        if (d.Revoked) failures.Add("DELEGATION_REVOKED");
        if (d.Agent != agent) failures.Add("DELEGATION_AGENT_MISMATCH");
        if (ns >= N(d.ExpiresAtNs)) failures.Add("DELEGATION_EXPIRED");
        if (N(intent.ExpiresAtNs) > N(d.ExpiresAtNs)) failures.Add("PAYMENT_OUTLIVES_DELEGATION");
        if (!d.Recipients.Contains(intent.Recipient)) failures.Add("DELEGATION_RECIPIENT_NOT_ALLOWED");
        if (amount > N(d.MaxPayment)) failures.Add("DELEGATION_PAYMENT_LIMIT");
        if (amount + N(d.Spent) > N(d.Budget)) failures.Add("DELEGATION_BUDGET_EXHAUSTED");
        if (amount + dailySpent > N(d.DailyLimit)) failures.Add("DELEGATION_DAILY_LIMIT");
        return failures.ToArray();
    }
}
