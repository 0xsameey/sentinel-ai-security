using System.Text.Json;
using Microsoft.Extensions.Options;
using Sentinel.Application.Security;
using Sentinel.Infrastructure.Near;

namespace Sentinel.Infrastructure.Security;

public sealed class NearDelegationReader(NearSecurityRpc rpc, IOptions<NearOptions> options) : IDelegationReader
{
    public async Task<string?> PaymentDelegationAsync(string id,string blockHash,CancellationToken ct) {
        var result=await rpc.ViewAsync(options.Value.ContractId,blockHash,"get_payment_delegation",new{id},ct);
        return result.ValueKind==JsonValueKind.Null?null:result.GetString();
    }
    public async Task<DelegationState?> ReadAsync(string id, string blockHash, CancellationToken ct)
    {
        var d = await rpc.ViewAsync(options.Value.ContractId, blockHash, "get_delegation", new { id }, ct);
        if (d.ValueKind == JsonValueKind.Null) return null;
        string S(string key) => d.GetProperty(key).GetString()!;
        return new(S("agent"), S("max_payment"), S("budget"), S("daily_limit"), S("expires_at_ns"),
            d.GetProperty("recipients").EnumerateArray().Select(x => x.GetString()!).ToArray(),
            d.GetProperty("revoked").GetBoolean(), S("spent"), S("day"), S("spent_today"));
    }
}
