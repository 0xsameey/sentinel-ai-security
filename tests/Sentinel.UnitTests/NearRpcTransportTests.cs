using Microsoft.Extensions.Options;
using Sentinel.Infrastructure.Near;

namespace Sentinel.UnitTests;

public class NearRpcTransportTests
{
    [Fact]
    public async Task Transport_failure_does_not_expose_upstream_credentials()
    {
        using var http = new HttpClient(new FailingHandler(new HttpRequestException("secret upstream URL")))
        { BaseAddress = new Uri("https://example.invalid") };
        var rpc = new NearRpcClient(http, Options.Create(new NearOptions { ContractId = "example.testnet" }));
        var error = await Assert.ThrowsAsync<NearRpcException>(() => rpc.CallFunctionAsync("get_config"));
        Assert.Equal("NEAR RPC transport unavailable.", error.Message);
        Assert.Null(error.InnerException);
    }

    [Fact]
    public async Task Upstream_timeout_is_distinct_from_caller_cancellation()
    {
        using var http = new HttpClient(new FailingHandler(new TaskCanceledException()))
        { BaseAddress = new Uri("https://example.invalid") };
        var rpc = new NearRpcClient(http, Options.Create(new NearOptions()));
        await Assert.ThrowsAsync<TimeoutException>(() => rpc.CallFunctionAsync("get_config"));
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => rpc.CallFunctionAsync("get_config", cancellationToken: cancellation.Token));
    }

    private sealed class FailingHandler(Exception error) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromException<HttpResponseMessage>(error);
    }
}
