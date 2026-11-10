
using System.Text.Json;
using Sentinel.Application.DTOs;
using Sentinel.Application.Interfaces;
using Sentinel.Domain.Entities;
using Sentinel.Domain.Enums;

namespace Sentinel.Infrastructure.Near;

public sealed class NearContractService : INearContractService
{
    private readonly NearRpcClient _rpcClient;

    public NearContractService(NearRpcClient rpcClient)
    {
        _rpcClient = rpcClient;
    }

    public async Task<ContractConfigDto> GetConfigAsync(
        CancellationToken cancellationToken = default)
    {
        var result = await _rpcClient.CallFunctionAsync(
            "get_config",
            cancellationToken: cancellationToken);

        // Rust returns a tuple: [owner, agent, paused]
        if (result.ValueKind != JsonValueKind.Array ||
            result.GetArrayLength() != 3)
        {
            throw new NearRpcException(
                "Unexpected contract configuration response.");
        }

        return new ContractConfigDto(
            result[0].GetString()!,
            result[1].GetString()!,
            result[2].GetBoolean());
    }

    public async Task<string> GetAvailableBalanceAsync(
        CancellationToken cancellationToken = default)
    {
        var result = await _rpcClient.CallFunctionAsync(
            "get_available_balance",
            cancellationToken: cancellationToken);

        return result.GetString()
            ?? throw new NearRpcException(
                "Invalid contract balance response.");
    }

    public async Task<Payment?> GetPaymentAsync(
        string paymentId,
        CancellationToken cancellationToken = default)
    {
        var result = await _rpcClient.CallFunctionAsync(
            "get_payment",
            new { id = paymentId },
            cancellationToken);

        if (result.ValueKind == JsonValueKind.Null)
            return null;

        var statusText = result
            .GetProperty("status")
            .GetString();

        if (!Enum.TryParse<PaymentStatus>(
                statusText, false, out var status) ||
            !Enum.IsDefined(status))
        {
            throw new NearRpcException(
                $"Unknown payment status: {statusText}");
        }

        return new Payment(
            result.GetProperty("id").GetString()!,
            result.GetProperty("recipient").GetString()!,
            result.GetProperty("amount_yocto").GetString()!,
            ulong.Parse(
                result.GetProperty("expires_at_ns").GetString()!,
                System.Globalization.CultureInfo.InvariantCulture),
            status);
    }
}
