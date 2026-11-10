
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;

namespace Sentinel.Infrastructure.Near;

public sealed class NearRpcClient
{
    private readonly HttpClient _httpClient;
    private readonly NearOptions _options;

    public NearRpcClient(
        HttpClient httpClient,
        IOptions<NearOptions> options)
    {
        _httpClient = httpClient;
        _options = options.Value;
    }

    public async Task<JsonElement> CallFunctionAsync(
        string methodName,
        object? args = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            return await CallFunctionCoreAsync(methodName, args, cancellationToken);
        }
        catch (HttpRequestException)
        {
            throw new NearRpcException("NEAR RPC transport unavailable.");
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new TimeoutException("NEAR RPC request timed out.");
        }
    }

    private async Task<JsonElement> CallFunctionCoreAsync(
        string methodName, object? args, CancellationToken cancellationToken)
    {
        var argsJson = JsonSerializer.Serialize(args ?? new { });
        var argsBase64 = Convert.ToBase64String(
            Encoding.UTF8.GetBytes(argsJson));

        var request = new
        {
            jsonrpc = "2.0",
            id = "sentinel-api",
            method = "query",
            @params = new
            {
                request_type = "call_function",
                finality = "final",
                account_id = _options.ContractId,
                method_name = methodName,
                args_base64 = argsBase64
            }
        };

        using var response = await _httpClient.PostAsJsonAsync(
            "",
            request,
            cancellationToken);

        response.EnsureSuccessStatusCode();

        using var document = await JsonDocument.ParseAsync(
            await response.Content.ReadAsStreamAsync(
                cancellationToken),
            cancellationToken: cancellationToken);

        var root = document.RootElement;

        if (root.TryGetProperty("error", out _))
        {
            throw new NearRpcException(
                "NEAR RPC rejected the view request.");
        }

        if (!root.TryGetProperty("result", out var result) ||
            !result.TryGetProperty("result", out var bytes) ||
            bytes.ValueKind != JsonValueKind.Array)
        {
            throw new NearRpcException(
                "Invalid NEAR RPC response.");
        }

        var decodedBytes = bytes.EnumerateArray()
            .Select(x => x.GetByte())
            .ToArray();

        var decodedJson = Encoding.UTF8.GetString(decodedBytes);

        using var decodedDocument = JsonDocument.Parse(decodedJson);

        return decodedDocument.RootElement.Clone();
    }
}

public sealed class NearRpcException : Exception
{
    public NearRpcException(string message) : base(message)
    {
    }
}
