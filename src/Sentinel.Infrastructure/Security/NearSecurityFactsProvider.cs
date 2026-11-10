using System.Text.Json;
using Microsoft.Extensions.Options;
using Sentinel.Application.Security;
using Sentinel.Infrastructure.Near;

namespace Sentinel.Infrastructure.Security;

public sealed class NearSecurityFactsProvider(NearSecurityRpc rpc, ICounterpartyHistory history,
    IOptions<NearOptions> options, TimeProvider clock) : ISecurityFactsProvider
{
    public async Task<SecurityFacts> FetchAsync(string recipient, CancellationToken ct = default)
    {
        var now = clock.GetUtcNow(); var contract = options.Value.ContractId;
        var indexed = await history.FetchAsync(recipient, ct);
        try
        {
            var status = await rpc.ReadAsync("status", Array.Empty<object>(), ct);
            if (status.GetProperty("chain_id").GetString() != "testnet" || !contract.EndsWith(".testnet", StringComparison.Ordinal)) throw new SecurityDataUnavailableException();
            var block = await rpc.ReadAsync("block", new { finality = "final" }, ct);
            var header = block.GetProperty("header"); var hash = header.GetProperty("hash").GetString()!;
            var blockTime = DateTimeOffset.FromUnixTimeMilliseconds(checked((long)(ulong.Parse(header.GetProperty("timestamp_nanosec").ToString()) / 1_000_000)));
            if (hash.Length < 20 || now - blockTime > TimeSpan.FromMinutes(2) || blockTime > now.AddSeconds(5)) throw new SecurityDataUnavailableException();
            var requests = new[] {
                rpc.ViewAsync(contract, hash, "get_config", new { }, ct), rpc.ViewAsync(contract, hash, "get_policy", new { }, ct),
                rpc.ViewAsync(contract, hash, "get_daily_usage", new { }, ct), rpc.ViewAsync(contract, hash, "get_available_balance", new { }, ct),
                rpc.ViewAsync(contract, hash, "is_recipient_allowed", new { recipient }, ct),
                rpc.QueryAsync(recipient, hash, "view_account", ct), rpc.QueryAsync(contract, hash, "view_account", ct)
            };
            var values = await Task.WhenAll(requests);
            var config = values[0]; var policy = values[1]; var usage = values[2];
            if (config.GetArrayLength() != 3 || usage.GetArrayLength() != 4) throw new SecurityDataUnavailableException();
            var exists = !values[5].TryGetProperty("missing_account", out _);
            var code = exists ? values[5].GetProperty("code_hash").GetString()! : "";
            var evidence = new[] {
                new RiskEvidence("NEAR_FINALIZED_STATE", rpc.Source, now, hash, JsonSerializer.Serialize(new {
                    Config = config, Policy = policy, Usage = usage, AvailableBalance = values[3], RecipientAllowed = values[4],
                    RecipientExists = exists, RecipientCodeHash = code, ContractCodeHash = values[6].GetProperty("code_hash") })),
                new RiskEvidence(indexed.Available ? "NEARBLOCKS_RECENT_SAMPLE" : "INDEXED_HISTORY_UNAVAILABLE", indexed.Source,
                    indexed.ObservedAtUtc, null, $"Bounded sample={indexed.SampleSize}; failed={indexed.FailedCount}; digest={indexed.Digest}")
            };
            return new(true, contract, config[0].GetString()!, config[1].GetString()!, config[2].GetBoolean(), exists, code,
                values[4].GetBoolean(), policy.GetProperty("max_per_transaction").GetString()!, policy.GetProperty("daily_limit").GetString()!,
                usage[1].GetString()!, usage[2].GetString()!, usage[3].GetString()!, usage[0].GetString()!, values[3].GetString()!,
                values[6].GetProperty("code_hash").GetString()!, hash, now, indexed, evidence);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch
        {
            return new(false, contract, "", "", true, false, "", false, "0", "0", "0", "0", "0", "0", "0", "", "", now, indexed,
                [new("CRITICAL_RPC_UNAVAILABLE", rpc.Source, now, null, "Finalized testnet state could not be established.")]);
        }
    }
}
