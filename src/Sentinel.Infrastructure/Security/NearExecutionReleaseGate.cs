using System.Numerics;
using Microsoft.Extensions.Configuration;
using Sentinel.Application.Security;

namespace Sentinel.Infrastructure.Security;

// Mandatory live attestation, not a configurable bypass. This reviewed hash
// contains the current 24-hour expiry guard; the known deployment does not match.
public sealed class NearExecutionReleaseGate(NearSecurityRpc rpc, IConfiguration config) : IExecutionReleaseGate
{
    public const string ReviewedCodeSha256 = "34d9ad95449b50a9ad4005d14a950b9d79b2b677ac5588aefc8640f61d15784f";
    public static readonly string ReviewedCodeHash = ToBase58(Convert.FromHexString(ReviewedCodeSha256));
    public async Task<string> VerifyAsync(SecurityFacts facts, CancellationToken ct = default)
    {
        return await VerifyCoreAsync(facts, facts.ContractCodeHash == NearDelegatedReleaseGate.ReviewedCodeHash ? NearDelegatedReleaseGate.ReviewedCodeHash : ReviewedCodeHash, "execute_payment", ct);
    }
    public async Task<string> VerifyCoreAsync(SecurityFacts facts, string codeHash, string method, CancellationToken ct, string publicKeyConfig = "SecurityRelease:AgentPublicKey")
    {
        if (!facts.Available || facts.ContractCodeHash != codeHash)
            throw new GatewayRejectedException("RELEASE_BLOCKED_DEPLOYED_EXPIRY_GUARD");
        var publicKey = config[publicKeyConfig];
        if (string.IsNullOrWhiteSpace(publicKey) || !publicKey.StartsWith("ed25519:", StringComparison.Ordinal))
            throw new GatewayRejectedException("RELEASE_BLOCKED_RESTRICTED_KEY_UNVERIFIED");
        try
        {
            var key = await rpc.ReadAsync("query", new { request_type = "view_access_key", block_id = facts.BlockHash,
                account_id = facts.Agent, public_key = publicKey }, ct);
            var permission = key.GetProperty("permission").GetProperty("FunctionCall");
            var methods = permission.GetProperty("method_names");
            if (permission.GetProperty("receiver_id").GetString() != facts.ContractId || methods.GetArrayLength() != 1 ||
                methods[0].GetString() != method) throw new SecurityDataUnavailableException();
            var price = await rpc.ReadAsync("gas_price", new { block_id = facts.BlockHash }, ct);
            if (!BigInteger.TryParse(price.GetProperty("gas_price").GetString(), out var gasPrice) || gasPrice <= 0 ||
                !BigInteger.TryParse(permission.GetProperty("allowance").GetString(), out var allowance) ||
                allowance < gasPrice * 30_000_000_000_000L)
                throw new SecurityDataUnavailableException();
            return publicKey;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch { throw new GatewayRejectedException("RELEASE_BLOCKED_RESTRICTED_KEY_UNVERIFIED"); }
    }
    private static string ToBase58(byte[] bytes)
    {
        const string alphabet = "123456789ABCDEFGHJKLMNPQRSTUVWXYZabcdefghijkmnopqrstuvwxyz";
        var number = new BigInteger(bytes, isUnsigned: true, isBigEndian: true); var result = "";
        while (number > 0) { result = alphabet[(int)(number % 58)] + result; number /= 58; }
        return result;
    }
}

