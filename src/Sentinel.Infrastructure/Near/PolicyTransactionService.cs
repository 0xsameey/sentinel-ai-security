
using System.Globalization;
using Microsoft.Extensions.Options;
using Sentinel.Application.DTOs;
using Sentinel.Application.Interfaces;

namespace Sentinel.Infrastructure.Near;

public sealed class PolicyTransactionService
    : IPolicyTransactionService
{
    private const string Gas = "30000000000000";

    private readonly NearRpcClient _rpc;
    private readonly NearOptions _options;

    public PolicyTransactionService(
        NearRpcClient rpc,
        IOptions<NearOptions> options)
    {
        _rpc = rpc;
        _options = options.Value;
    }

    public async Task<ContractCallDto> PreparePolicyUpdateAsync(
        string accountId,
        UpdateSpendingPolicyRequest request,
        CancellationToken cancellationToken = default)
    {
        await RequireOwnerAsync(
            accountId, cancellationToken);

        if (!ulong.TryParse(
                request.MaxPerTransactionYocto,
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out _) &&
            !System.Numerics.BigInteger.TryParse(
                request.MaxPerTransactionYocto,
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out _))
        {
            throw new ArgumentException(
                "Invalid maximum transaction amount.");
        }

        if (!System.Numerics.BigInteger.TryParse(
                request.MaxPerTransactionYocto,
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out var max) ||
            !System.Numerics.BigInteger.TryParse(
                request.DailyLimitYocto,
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out var daily) ||
            max <= 0 ||
            daily < max ||
            max > (System.Numerics.BigInteger.One << 128) - 1 ||
            daily > (System.Numerics.BigInteger.One << 128) - 1)
        {
            throw new ArgumentException(
                "Invalid spending limits.");
        }

        return new ContractCallDto(
            _options.ContractId,
            "set_policy",
            new
            {
                max_per_transaction =
                    request.MaxPerTransactionYocto,
                daily_limit = request.DailyLimitYocto
            },
            Gas,
            "0");
    }

    public async Task<ContractCallDto> PrepareRecipientUpdateAsync(
        string accountId,
        string recipient,
        bool allowed,
        CancellationToken cancellationToken = default)
    {
        await RequireOwnerAsync(
            accountId, cancellationToken);

        if (string.IsNullOrWhiteSpace(recipient) ||
            recipient.Length > 64 ||
            recipient != recipient.ToLowerInvariant() ||
            !System.Text.RegularExpressions.Regex.IsMatch(
                recipient,
                @"^(?=.{2,64}$)[a-z0-9]+(?:[-_.][a-z0-9]+)*$"))
        {
            throw new ArgumentException(
                "Invalid recipient account.");
        }

        return new ContractCallDto(
            _options.ContractId,
            "set_recipient_allowed",
            new
            {
                recipient,
                allowed
            },
            Gas,
            "0");
    }

    private async Task RequireOwnerAsync(
        string accountId,
        CancellationToken cancellationToken)
    {
        var config = await _rpc.CallFunctionAsync(
            "get_config",
            cancellationToken: cancellationToken);

        var owner = config[0].GetString();

        if (!string.Equals(
            owner, accountId, StringComparison.Ordinal))
        {
            throw new UnauthorizedAccessException(
                "Only the contract owner may update policies.");
        }
    }
}
