
using System.Globalization;
using System.Numerics;
using Sentinel.Application.DTOs;
using Sentinel.Application.Interfaces;

namespace Sentinel.Infrastructure.AI;

public sealed class RiskAssessmentService : IRiskAssessmentService
{
    private readonly ISpendingPolicyService _policies;
    private readonly IAiExplanationService _ai;

    public RiskAssessmentService(
        ISpendingPolicyService policies,
        IAiExplanationService ai)
    {
        _policies = policies;
        _ai = ai;
    }

    public async Task<RiskAssessmentDto> AssessAsync(
        AssessTransactionRequest request,
        CancellationToken cancellationToken = default)
    {
        if (request is null ||
            string.IsNullOrWhiteSpace(request.Recipient) ||
            request.Recipient.Length > 64 ||
            string.IsNullOrWhiteSpace(request.Purpose) ||
            request.Purpose.Length > 500)
            throw new ArgumentException("Invalid assessment request.");

        var maxU128 = (BigInteger.One << 128) - 1;

        if (string.IsNullOrEmpty(request.AmountYocto) ||
            request.AmountYocto.Any(c => c < '0' || c > '9') ||
            !BigInteger.TryParse(
                request.AmountYocto, NumberStyles.None,
                CultureInfo.InvariantCulture, out var amount) ||
            amount <= 0 || amount > maxU128)
            throw new ArgumentException("Invalid transaction amount.");

        var policy = await _policies.GetPolicyAsync(cancellationToken);
        var recipient = await _policies.GetRecipientStatusAsync(
            request.Recipient, cancellationToken);

        var max = BigInteger.Parse(policy.MaxPerTransactionYocto);
        var daily = BigInteger.Parse(policy.DailyLimitYocto);

        var reasons = new List<string>();
        var blocked = false;
        var score = 0;

        if (!recipient.IsAllowed)
        {
            blocked = true;
            reasons.Add("Recipient is not on the approved allowlist.");
        }

        if (max == 0 || daily == 0 || amount > max || amount > daily)
        {
            blocked = true;
            reasons.Add("Transaction violates configured spending limits.");
        }

        if (!blocked)
        {
            reasons.Add("Recipient and transaction amount pass basic policy checks.");

            // Use integer arithmetic to avoid loss of precision.
            if (amount * 100 >= max * 80)
            {
                score += 35;
                reasons.Add("Amount is at least 80% of the transaction limit.");
            }

            if (amount * 100 >= daily * 50)
            {
                score += 20;
                reasons.Add("Amount consumes at least 50% of daily allowance.");
            }
        }

        if (blocked)
            score = 100;

        var decision = blocked ? "BLOCK"
            : score >= 30 ? "REVIEW" : "ALLOW";

        var level = score >= 70 ? "High"
            : score >= 30 ? "Medium" : "Low";

        // AI does not change the deterministic decision.
        var explanation = await _ai.ExplainAsync(
            request, decision, reasons, cancellationToken);

        return new RiskAssessmentDto(
            score, level, decision, reasons, explanation);
    }
}
