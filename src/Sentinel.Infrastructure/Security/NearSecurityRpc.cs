using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Options;
using Sentinel.Infrastructure.Near;

namespace Sentinel.Infrastructure.Security;

public sealed class SecurityDataUnavailableException : Exception;
public sealed class NearSecurityRpc(HttpClient http, IOptions<NearOptions> options)
{
    // Provider paths, user info and query strings may contain API credentials.
    public string Source => new Uri(options.Value.RpcUrl).GetComponents(UriComponents.SchemeAndServer, UriFormat.UriEscaped);
    public async Task<JsonElement> ReadAsync(string method, object parameters, CancellationToken ct)
    {
        if (method is not ("query" or "status" or "block" or "gas_price")) throw new InvalidOperationException("Read-only RPC.");
        using var response = await http.PostAsJsonAsync("", new { jsonrpc = "2.0", id = "sentinel-security", method, @params = parameters }, ct);
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadAsByteArrayAsync(ct);
        if (body.Length > 1_000_000) throw new SecurityDataUnavailableException();
        using var doc = JsonDocument.Parse(body);
        var root = doc.RootElement;
        if (root.TryGetProperty("error", out var error))
        {
            // Only the explicit unknown-account error is evidence of nonexistence.
            if (error.TryGetProperty("cause", out var cause) && cause.TryGetProperty("name", out var name) && name.GetString() == "UNKNOWN_ACCOUNT")
                return JsonSerializer.SerializeToElement(new { missing_account = true });
            throw new SecurityDataUnavailableException();
        }
        if (!root.TryGetProperty("result", out var result) || result.ValueKind != JsonValueKind.Object || result.TryGetProperty("error", out _))
            throw new SecurityDataUnavailableException();
        return result.Clone();
    }
    public Task<JsonElement> QueryAsync(string account, string block, string type, CancellationToken ct) =>
        ReadAsync("query", new { request_type = type, block_id = block, account_id = account }, ct);
    public async Task<JsonElement> ViewAsync(string account, string block, string method, object args, CancellationToken ct)
    {
        var result = await ReadAsync("query", new { request_type = "call_function", block_id = block, account_id = account,
            method_name = method, args_base64 = Convert.ToBase64String(JsonSerializer.SerializeToUtf8Bytes(args)) }, ct);
        var bytes = result.GetProperty("result").EnumerateArray().Select(x => x.GetByte()).ToArray();
        using var doc = JsonDocument.Parse(bytes);
        return doc.RootElement.Clone();
    }
}
