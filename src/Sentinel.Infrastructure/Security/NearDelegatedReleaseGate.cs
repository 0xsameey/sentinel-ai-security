using Microsoft.Extensions.Configuration;
using Sentinel.Application.Security;
namespace Sentinel.Infrastructure.Security;
public sealed class NearDelegatedReleaseGate(NearSecurityRpc rpc,IConfiguration config) : IDelegatedReleaseGate {
    // Updated only after a reproducible local WASM build; empty pins fail closed.
    public const string ReviewedCodeHash="5JGbnZu5NUppt922dqjtAH6oTixkVkXEKcynUqjjHTyE";
    public Task<string> VerifyAsync(SecurityFacts facts,CancellationToken ct)=>
        new NearExecutionReleaseGate(rpc,config).VerifyCoreAsync(facts,ReviewedCodeHash,"execute_delegated_payment",ct,"SecurityRelease:DelegatedAgentPublicKey");
}
