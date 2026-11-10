using System.Security.Claims;
using System.Security.Cryptography;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;
using Sentinel.Application.Interfaces;

namespace Sentinel.Api.Authentication;

// Assessment-only credential. It cannot approve risk, prepare owner transactions,
// consume worker permits or access signing keys. No credential is provisioned here.
public sealed class GatewayAgentHandler(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger,
    UrlEncoder encoder, IConfiguration config, INearContractService contract) : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    public const string SchemeName = "SentinelAgent";
    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var supplied = Request.Headers["X-Sentinel-Agent-Key"].ToString();
        if (string.IsNullOrEmpty(supplied)) return AuthenticateResult.NoResult();
        try
        {
            var expected = Convert.FromBase64String(config["AgentGateway:ApiKey"] ?? "");
            var actual = Convert.FromBase64String(supplied);
            try {
                if (expected.Length < 32 || actual.Length != expected.Length || !CryptographicOperations.FixedTimeEquals(actual, expected))
                    return AuthenticateResult.Fail("Invalid agent credential.");
            } finally { CryptographicOperations.ZeroMemory(actual); CryptographicOperations.ZeroMemory(expected); }
        }
        catch (FormatException) { return AuthenticateResult.Fail("Invalid agent credential."); }
        var current = await contract.GetConfigAsync(Context.RequestAborted);
        var principal = new ClaimsPrincipal(new ClaimsIdentity([new("sub", current.Agent), new("role", "agent")], SchemeName));
        return AuthenticateResult.Success(new AuthenticationTicket(principal, SchemeName));
    }
}
