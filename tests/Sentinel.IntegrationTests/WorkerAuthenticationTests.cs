using System.Security.Cryptography;
using Microsoft.Extensions.Configuration;
using Sentinel.Api.Authentication;

namespace Sentinel.IntegrationTests;

public sealed class WorkerAuthenticationTests
{
    [Theory]
    [InlineData(32)]
    [InlineData(48)]
    [InlineData(64)]
    public void Configured_worker_keys_authenticate_and_wrong_keys_fail(int length)
    {
        var key = Convert.ToBase64String(RandomNumberGenerator.GetBytes(length));
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["AgentWorker:ApiKey"] = key }).Build();
        var auth = new AgentWorkerAuthenticator(configuration);
        Assert.True(auth.IsValid(key));
        Assert.False(auth.IsValid(null));
        Assert.False(auth.IsValid("not-base64"));
        Assert.False(auth.IsValid(Convert.ToBase64String(RandomNumberGenerator.GetBytes(length))));
        Assert.False(auth.IsValid(Convert.ToBase64String(new byte[16])));
    }
}
