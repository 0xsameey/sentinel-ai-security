using System.Globalization;
using System.Net.Http.Headers;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Sentinel.Application.Security;

namespace Sentinel.Infrastructure.Security;

// NearBlocks v3 bounded recent sample. It is NOT account-creation or scam proof.
public sealed class NearblocksHistory(HttpClient http, IConfiguration config, TimeProvider clock) : ICounterpartyHistory
{
    public async Task<HistorySignal> FetchAsync(string recipient, CancellationToken ct = default)
    {
        var now = clock.GetUtcNow();
        var source = "nearblocks:v3:unconfigured";
        try
        {
            var configured = config["CounterpartyIndex:BaseUrl"] ?? "https://api-testnet.nearblocks.io/";
            if (!Uri.TryCreate(configured, UriKind.Absolute, out var baseUri) || baseUri.Scheme != "https" ||
                baseUri.UserInfo.Length > 0 || baseUri.Query.Length > 0) return new(false, 0, 0, "unavailable", now, source);
            // HttpClient BaseAddress can be overridden by TEST composition only.
            var relative = $"v3/accounts/{Uri.EscapeDataString(recipient)}/txns?limit=10";
            source = new Uri(baseUri, relative).ToString();
            using var request = new HttpRequestMessage(HttpMethod.Get, http.BaseAddress is null ? new Uri(baseUri, relative) : new Uri(http.BaseAddress, relative));
            if (!string.IsNullOrWhiteSpace(config["CounterpartyIndex:ApiKey"]))
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", config["CounterpartyIndex:ApiKey"]);
            using var response = await http.SendAsync(request, ct);
            response.EnsureSuccessStatusCode();
            var bytes = await response.Content.ReadAsByteArrayAsync(ct);
            if (bytes.Length > 1_000_000) throw new SecurityDataUnavailableException();
            using var doc = JsonDocument.Parse(bytes);
            if (doc.RootElement.TryGetProperty("errors", out var errors) && errors.ValueKind == JsonValueKind.Array && errors.GetArrayLength() > 0)
                throw new SecurityDataUnavailableException();
            var data = doc.RootElement.GetProperty("data");
            if (data.ValueKind != JsonValueKind.Array || data.GetArrayLength() > 10) throw new SecurityDataUnavailableException();
            var failed = 0;
            var normalized = new List<object>();
            foreach (var tx in data.EnumerateArray())
            {
                var hash = tx.GetProperty("transaction_hash").GetString();
                if (hash is null || hash.Length is < 20 or > 128) throw new SecurityDataUnavailableException();
                var timestamp = tx.GetProperty("block").GetProperty("block_timestamp").ToString();
                if (!ulong.TryParse(timestamp, NumberStyles.None, CultureInfo.InvariantCulture, out var nanos) ||
                    nanos / 1_000_000 > (ulong)now.AddMinutes(5).ToUnixTimeMilliseconds()) throw new SecurityDataUnavailableException();
                var success = tx.GetProperty("outcomes").GetProperty("status").GetBoolean();
                if (!success) failed++;
                normalized.Add(new { hash, timestamp, success });
            }
            return new(true, data.GetArrayLength(), failed, GatewayPaymentRequest.HashText(JsonSerializer.Serialize(normalized)), now, source);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch { return new(false, 0, 0, "unavailable", now, source); }
    }
}
