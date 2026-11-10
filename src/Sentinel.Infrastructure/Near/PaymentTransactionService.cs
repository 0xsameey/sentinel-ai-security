using Sentinel.Application.Security;
﻿
using System.Globalization;
using System.Numerics;
using Sentinel.Application.DTOs;
using Sentinel.Application.Interfaces;
using Sentinel.Domain.Enums;
using Microsoft.Extensions.Options;

namespace Sentinel.Infrastructure.Near;

public sealed class PaymentTransactionService
    : IPaymentTransactionService
{
    private const string Gas = "30000000000000";

    private static readonly BigInteger MaxU128 =
        (BigInteger.One << 128) - 1;

    private readonly INearContractService _contract;
    private readonly ISpendingPolicyService _policies;
    private readonly NearOptions _options;
    private readonly ISecurityGateway _gateway;

    public PaymentTransactionService(
        INearContractService contract,
        ISpendingPolicyService policies,
        IOptions<NearOptions> options, ISecurityGateway gateway)
    {
        _contract = contract;
        _policies = policies;
        _options = options.Value;
        _gateway = gateway;
    }

    public async Task<PreparedPaymentDto>
        PrepareAuthorizationAsync(
            string ownerAccountId,
            AuthorizePaymentRequest request,
            CancellationToken cancellationToken = default, string? paymentId = null)
    {
        var config = await _contract.GetConfigAsync(
            cancellationToken);

        if (config.Owner != ownerAccountId)
            throw new UnauthorizedAccessException(
                "Only the contract owner can authorize payments.");

        if (config.Paused)
            throw new InvalidOperationException(
                "Contract is paused.");

        if (string.IsNullOrWhiteSpace(request.Recipient) ||
            request.Recipient.Length > 64 ||
            request.Recipient == _options.ContractId)
            throw new ArgumentException(
                "Invalid recipient.");

        var amount = ParseYocto(request.AmountYocto);

        var policy = await _policies.GetPolicyAsync(
            cancellationToken);

        var maxAmount = ParseYocto(
            policy.MaxPerTransactionYocto);

        if (maxAmount == 0 || amount > maxAmount)
            throw new ArgumentException(
                "Payment exceeds the transaction limit.");

        var nowNs = checked(
            (ulong)DateTimeOffset.UtcNow
                .ToUnixTimeMilliseconds() * 1_000_000UL);

        var maxExpiryNs = checked(
            nowNs + 86_400_000_000_000UL);

        if (request.ExpiresAtNs <= nowNs ||
            request.ExpiresAtNs > maxExpiryNs)
            throw new ArgumentException(
                "Expiry must be within the next 24 hours.");

        var recipient = await _policies
            .GetRecipientStatusAsync(
                request.Recipient,
                cancellationToken);

        if (!recipient.IsAllowed)
            throw new ArgumentException(
                "Recipient is not approved.");

        paymentId ??= Guid.NewGuid().ToString("N");
        if (!Guid.TryParseExact(paymentId, "N", out _)) throw new ArgumentException("Invalid payment ID.");

        var call = new ContractCallDto(
            _options.ContractId,
            "authorize_payment",
            new
            {
                id = paymentId,
                recipient = request.Recipient,
                amount_yocto = amount.ToString(
                    CultureInfo.InvariantCulture),
                expires_at_ns =
                    request.ExpiresAtNs.ToString(
                        CultureInfo.InvariantCulture)
            },
            Gas,
            "0"
        );

        return new PreparedPaymentDto(paymentId, call);
    }

    public Task<ContractCallDto> PrepareExecutionAsync(string agentAccountId, string paymentId,
        CancellationToken cancellationToken = default) => _gateway.PrepareExecutionAsync(agentAccountId, paymentId, cancellationToken);

    private static BigInteger ParseYocto(string? value)
    {
        if (string.IsNullOrWhiteSpace(value) ||
            value.Any(c => c < '0' || c > '9') ||
            !BigInteger.TryParse(
                value,
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out var amount) ||
            amount <= 0 ||
            amount > MaxU128)
        {
            throw new ArgumentException(
                "Invalid yoctoNEAR amount.");
        }

        return amount;
    }
}
