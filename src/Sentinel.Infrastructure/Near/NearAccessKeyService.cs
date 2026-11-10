
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Options;

namespace Sentinel.Infrastructure.Near;

public sealed class NearAccessKeyService
{
    private readonly HttpClient _client;
    private readonly NearOptions _options;

    public NearAccessKeyService(
        HttpClient client,
        IOptions<NearOptions> options)
    {
        _client = client;
        _options = options.Value;
    }

    public async Task<bool> IsAuthorizedAsync(
        string accountId,
        string publicKey,
        CancellationToken cancellationToken = default)
    {
        var request = new
        {
            jsonrpc = "2.0",
            id = "sentinel-auth",
            method = "query",
            @params = new
            {
                request_type = "view_access_key",
                finality = "final",
                account_id = accountId,
                public_key = publicKey
            }
        };

        using var response = await _client.PostAsJsonAsync(
            "",
            request,
            cancellationToken);

        response.EnsureSuccessStatusCode();

        using var document = await JsonDocument.ParseAsync(
            await response.Content.ReadAsStreamAsync(
                cancellationToken),
            cancellationToken: cancellationToken);

        var root = document.RootElement;

        if (root.TryGetProperty("error", out var error))
        {
            // An unknown account/key is rejected.
            // Infrastructure failures must not be treated
            // as invalid signatures.
            var errorText = error.GetRawText();

            if (errorText.Contains("UNKNOWN_ACCESS_KEY",
                    StringComparison.OrdinalIgnoreCase) ||
                errorText.Contains("UNKNOWN_ACCOUNT",
                    StringComparison.OrdinalIgnoreCase))
                return false;

            throw new NearRpcException(
                "NEAR access-key lookup failed.");
        }

        if (!root.TryGetProperty("result", out var result) ||
            !result.TryGetProperty("permission", out var permission))
        {
            throw new NearRpcException(
                "Invalid access-key lookup response.");
        }

        return permission.ValueKind == JsonValueKind.String &&
               permission.GetString() == "FullAccess";
    }
}
