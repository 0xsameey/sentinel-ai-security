
using System.Globalization;
using System.Text.Json;
using Sentinel.Application.DTOs;
using Sentinel.Application.Interfaces;

namespace Sentinel.Infrastructure.Near;

public sealed class SpendingPolicyService
    : ISpendingPolicyService
{
    private readonly NearRpcClient _rpcClient;

    public SpendingPolicyService(NearRpcClient rpcClient)
    {
        _rpcClient = rpcClient;
    }

    public async Task<SpendingPolicyDto> GetPolicyAsync(
        CancellationToken cancellationToken = default)
    {
        var result = await _rpcClient.CallFunctionAsync(
            "get_policy",
            cancellationToken: cancellationToken);

        return new SpendingPolicyDto(
            result.GetProperty("max_per_transaction").GetString()
                ?? throw new NearRpcException("Invalid maximum amount."),
            result.GetProperty("daily_limit").GetString()
                ?? throw new NearRpcException("Invalid daily limit.")
        );
    }

    public async Task<DailyUsageDto> GetDailyUsageAsync(
        CancellationToken cancellationToken = default)
    {
        var result = await _rpcClient.CallFunctionAsync(
            "get_daily_usage",
            cancellationToken: cancellationToken);

        if (result.ValueKind != JsonValueKind.Array ||
            result.GetArrayLength() != 4)
        {
            throw new NearRpcException(
                "Unexpected daily usage response.");
        }

        return new DailyUsageDto(
            ulong.Parse(
                result[0].GetString()!,
                CultureInfo.InvariantCulture),
            result[1].GetString()!,
            result[2].GetString()!,
            result[3].GetString()!
        );
    }

    public async Task<RecipientStatusDto> GetRecipientStatusAsync(
        string accountId,
        CancellationToken cancellationToken = default)
    {
        var result = await _rpcClient.CallFunctionAsync(
            "is_recipient_allowed",
            new { recipient = accountId },
            cancellationToken);

        if (result.ValueKind is not
            (JsonValueKind.True or JsonValueKind.False))
        {
            throw new NearRpcException(
                "Unexpected recipient permission response.");
        }

        return new RecipientStatusDto(
            accountId,
            result.GetBoolean());
    }
}
