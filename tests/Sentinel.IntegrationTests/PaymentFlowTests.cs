using Sentinel.Application.Security;
using Sentinel.Infrastructure.Security;
using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Sentinel.Api.Authentication;
using Sentinel.Infrastructure.Near;
using Sentinel.Infrastructure.Persistence;
using Npgsql;
using System.Text.RegularExpressions;

namespace Sentinel.IntegrationTests;

public sealed class LocalE2EFactAttribute : FactAttribute
{
    public LocalE2EFactAttribute()
    {
        if (Environment.GetEnvironmentVariable("SENTINEL_E2E_DB") is null)
            Skip = "Run tests/run-milestone1.ps1 to provision isolated PostgreSQL and NEAR simulation.";
    }
}

public sealed class PaymentFlowTests
{
    [LocalE2EFact]
    public async Task Owner_authorization_agent_dispatch_verification_and_reconciliation()
    {
        var root = Environment.GetEnvironmentVariable("SENTINEL_E2E_ROOT")!;
        var nearUrl = Environment.GetEnvironmentVariable("SENTINEL_E2E_NEAR")!;
        var apiUrl = Environment.GetEnvironmentVariable("SENTINEL_E2E_API")!;
        var signerPort = Environment.GetEnvironmentVariable("SENTINEL_E2E_SIGNER_PORT")!;
        var databaseConnection = Environment.GetEnvironmentVariable("SENTINEL_E2E_DB")!;
        var settings = new NpgsqlConnectionStringBuilder(databaseConnection);
        var schema = settings.SearchPath;
        if (schema is not null && !Regex.IsMatch(schema, "^sentinel_e2e_[a-z0-9_]+$"))
            throw new InvalidOperationException("Tests require a dedicated sentinel_e2e_ search path without public fallback.");
        var workerKey = Convert.ToBase64String(RandomNumberGenerator.GetBytes(48));
        var dispatchKey = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
        var args = new[] {
            "--environment=Testing", $"--urls={apiUrl}",
            $"--ConnectionStrings:SentinelDb={Environment.GetEnvironmentVariable("SENTINEL_E2E_DB")}",
            "--Near:RpcUrl=https://rpc.testnet.invalid", "--Near:ContractId=treasury.testnet",
            "--CounterpartyIndex:BaseUrl=https://index.testnet.invalid/", "--SecurityRelease:AgentPublicKey=ed25519:test-signer",
            "--Auth:Issuer=sentinel-tests", "--Auth:Audience=sentinel-tests", "--Auth:Recipient=sentinel-tests",
            "--Auth:AccessTokenMinutes=5", $"--Auth:JwtSigningKey={Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))}",
            $"--AgentWorker:ApiKey={workerKey}", $"--AgentDispatcher:Key={dispatchKey}",
            $"--AgentDispatcher:Url=http://127.0.0.1:{signerPort}", "--OpenAI:ApiKey=", "--OpenAI:Model=",
        };
        await using var app = SentinelHost.Build(args, builder => {
            // Override transport only, after production HTTPS option validation.
            // No production option or environment flag permits HTTP RPC.
            builder.Services.AddHttpClient<NearSecurityRpc>(c => c.BaseAddress = new Uri(nearUrl + "/"));
            builder.Services.AddHttpClient<ICounterpartyHistory, NearblocksHistory>(c => c.BaseAddress = new Uri(nearUrl + "/"));
            builder.Services.AddHttpClient<NearRpcClient>(c => c.BaseAddress = new Uri(nearUrl + "/"));
            builder.Services.AddHttpClient<NearTransactionVerifier>(c => c.BaseAddress = new Uri(nearUrl + "/"));
            if (schema is not null)
                builder.Services.AddDbContext<SentinelDbContext>(options => options.UseNpgsql(databaseConnection,
                    pg => pg.MigrationsHistoryTable("__EFMigrationsHistory", schema)));
        });
        using (var scope = app.Services.CreateScope())
        {
            var database = scope.ServiceProvider.GetRequiredService<SentinelDbContext>();
            if (schema is not null)
            {
                // Fail before any migration if the connection could touch public.
                Assert.Equal(schema, await database.Database.SqlQueryRaw<string>("SELECT current_schema()::text AS \"Value\"").SingleAsync());
                Assert.Equal(0, await database.Database.SqlQueryRaw<int>(
                    "SELECT count(*)::int AS \"Value\" FROM information_schema.tables WHERE table_schema = current_schema()").SingleAsync());
            }
            await database.Database.MigrateAsync();
        }
        await app.StartAsync();
        var start = new ProcessStartInfo("node") { UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardOutput = true, RedirectStandardError = true, WorkingDirectory = root };
        start.ArgumentList.Add(Path.Combine(root, "src/sentinel-signer/dist/tests/worker-host.js"));
        start.Environment["TEST_API_URL"] = apiUrl;
        start.Environment["TEST_NEAR_URL"] = nearUrl;
        start.Environment["TEST_WORKER_KEY"] = workerKey;
        start.Environment["TEST_DISPATCH_KEY"] = dispatchKey;
        start.Environment["TEST_SIGNER_PORT"] = signerPort;
        using var signer = Process.Start(start)!;
        try
        {
            var ready = await signer.StandardOutput.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(20));
            Assert.Equal("TEST_SIGNER_READY", ready);
            using var client = new HttpClient { BaseAddress = new Uri(apiUrl), Timeout = TimeSpan.FromSeconds(30) };
            using var fixture = new HttpClient { BaseAddress = new Uri(nearUrl + "/") };
            Task<HttpResponseMessage> Seed(object body) => fixture.PostAsJsonAsync("control", body, new JsonSerializerOptions());
            (await Seed(new { keyMethods = new[] { "execute_payment" }, agent = "agent.testnet", historyUnavailable = false, keyMissing = false, keyFullAccess = false, rpcUnavailable = false, paused = false,
                policy = new { max_per_transaction = "1000", daily_limit = "10000" }, usage = new[] { "1","0","0","0" }, balance = "1000000",
                contractCodeHash = NearExecutionReleaseGate.ReviewedCodeHash, recipientAllowed = (bool?)null })).EnsureSuccessStatusCode();
            var tokens = app.Services.GetRequiredService<JwtTokenService>();
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", tokens.CreateToken("owner.testnet").Token);
            var expiry = checked((ulong)DateTimeOffset.UtcNow.AddHours(1).ToUnixTimeMilliseconds() * 1_000_000UL + 123UL);
            var intent = new { recipient = "merchant.testnet", amountYocto = "000100", expiresAtNs = expiry.ToString(), purpose = "Pay for API credits" };
            var idempotency = Guid.NewGuid().ToString();
            async Task<HttpResponseMessage> Prepare(object body, string key) {
                var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/payments/prepare") { Content = JsonContent.Create(body) };
                request.Headers.Add("Idempotency-Key", key); return await client.SendAsync(request);
            }
            using var preparedResponse = await Prepare(intent, idempotency);
            Assert.Equal(HttpStatusCode.OK, preparedResponse.StatusCode);
            var prepared = await preparedResponse.Content.ReadFromJsonAsync<JsonElement>();
            var id = prepared.GetProperty("paymentId").GetString()!;
            var call = prepared.GetProperty("transaction");
            Assert.Equal("100", call.GetProperty("args").GetProperty("amount_yocto").GetString());
            using var retry = await Prepare(intent, idempotency);
            Assert.Equal(id, (await retry.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("paymentId").GetString());
            using var conflict = await Prepare(intent with { amountYocto = "101" }, idempotency);
            Assert.Equal(HttpStatusCode.Conflict, conflict.StatusCode);
            using var blocked = await Prepare(intent with { recipient = "attacker.testnet" }, Guid.NewGuid().ToString());
            Assert.Equal(HttpStatusCode.UnprocessableEntity, blocked.StatusCode);
            using var review = await Prepare(intent with { amountYocto = "900" }, Guid.NewGuid().ToString());
            Assert.Equal(HttpStatusCode.UnprocessableEntity, review.StatusCode);
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", tokens.CreateToken("attacker.testnet").Token);
            using var forbidden = await Prepare(intent, Guid.NewGuid().ToString());
            Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode);
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", tokens.CreateToken("owner.testnet").Token);

            var authorizationHash = Guid.NewGuid().ToString("N");
            object Transaction(string hash, string recipient, string paymentId) => new {
                final_execution_status = "FINAL", status = new { SuccessValue = "" },
                transaction = new { hash, signer_id = "owner.testnet", receiver_id = "treasury.testnet",
                    actions = new[] { new { FunctionCall = new { method_name = "authorize_payment", deposit = "0",
                        args = Convert.ToBase64String(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new {
                            id = paymentId, recipient, amount_yocto = "100", expires_at_ns = expiry.ToString() }))) } } } }
            };
            var onChain = new { id, recipient = "merchant.testnet", amount_yocto = "100", expires_at_ns = expiry.ToString(), status = "Authorized" };
            (await Seed(new { payment = onChain, transaction = Transaction(authorizationHash, "merchant.testnet", id) })).EnsureSuccessStatusCode();
            using var wrongOperation = await client.PostAsJsonAsync($"/api/v1/payments/{id}/transactions", new { transactionHash = authorizationHash, operation = "authorize_payment" });
            Assert.Equal(HttpStatusCode.BadRequest, wrongOperation.StatusCode);
            using var authorization = await client.PostAsJsonAsync($"/api/v1/payments/{id}/transactions", new { transactionHash = authorizationHash, operation = "Authorization" });
            Assert.Equal(HttpStatusCode.OK, authorization.StatusCode);
            var forgedHash = Guid.NewGuid().ToString("N");
            (await Seed(new { transaction = Transaction(forgedHash, "attacker.testnet", id) })).EnsureSuccessStatusCode();
            using var forged = await client.PostAsJsonAsync($"/api/v1/payments/{id}/transactions", new { transactionHash = forgedHash, operation = "Authorization" });
            Assert.Equal(HttpStatusCode.Conflict, forged.StatusCode);
            using var paymentResponse = await client.GetAsync($"/api/v1/payments/{id}");
            Assert.Equal(expiry.ToString(), (await paymentResponse.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("expiresAtNs").GetString());
            using var denied = await client.PostAsJsonAsync($"/api/internal/agent/payments/{id}/prepare-execution", new { });
            Assert.Equal(HttpStatusCode.Unauthorized, denied.StatusCode);
            (await Seed(new { paused = true })).EnsureSuccessStatusCode();
            using (var pausedRequest = new HttpRequestMessage(HttpMethod.Post, $"/api/internal/agent/payments/{id}/prepare-execution")) {
                pausedRequest.Headers.Add("X-Sentinel-Worker-Key", workerKey);
                using var paused = await client.SendAsync(pausedRequest);
                Assert.Equal(HttpStatusCode.Conflict, paused.StatusCode);
            }
            (await Seed(new { paused = false })).EnsureSuccessStatusCode();
            using var dispatched = await client.PostAsync($"/api/v1/payments/{id}/dispatch", null);
            Assert.True(dispatched.IsSuccessStatusCode, await dispatched.Content.ReadAsStringAsync());
            using var tracking = await client.GetAsync($"/api/v1/payments/{id}/tracking");
            Assert.Equal("Completed", (await tracking.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("status").GetString());
            using var duplicate = await client.PostAsync($"/api/v1/payments/{id}/dispatch", null);
            Assert.Equal(HttpStatusCode.Conflict, duplicate.StatusCode);

            // Recovery: a second transfer completes while its execution report is
            // lost. A fresh DB scope + sync recovers state, and reporting its
            // EXISTING hash restores the attempt without another submission.
            using var recoveryPrepared = await Prepare(intent, Guid.NewGuid().ToString());
            Assert.Equal(HttpStatusCode.OK, recoveryPrepared.StatusCode);
            var recoveryId = (await recoveryPrepared.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("paymentId").GetString()!;
            var recoveryAuthHash = Guid.NewGuid().ToString("N");
            (await Seed(new { payment = onChain with { id = recoveryId },
                transaction = Transaction(recoveryAuthHash, "merchant.testnet", recoveryId) })).EnsureSuccessStatusCode();
            (await client.PostAsJsonAsync($"/api/v1/payments/{recoveryId}/transactions",
                new { transactionHash = recoveryAuthHash, operation = "Authorization" })).EnsureSuccessStatusCode();
            var recoveryHash = Guid.NewGuid().ToString("N");
            (await Seed(new {
                payment = onChain with { id = recoveryId, status = "Completed" },
                transaction = new { final_execution_status = "FINAL", status = new { SuccessValue = "" },
                    transaction = new { hash = recoveryHash, signer_id = "agent.testnet", receiver_id = "treasury.testnet",
                        actions = new[] { new { FunctionCall = new { method_name = "execute_payment", deposit = "0",
                            args = Convert.ToBase64String(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new { id = recoveryId }))) } } } } }
            })).EnsureSuccessStatusCode();
            (await client.PostAsync($"/api/v1/payments/{recoveryId}/sync", null)).EnsureSuccessStatusCode();
            using (var recovered = await client.GetAsync($"/api/v1/payments/{recoveryId}/tracking"))
                Assert.Equal("Completed", (await recovered.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("status").GetString());
            using (var recoveryDuplicate = await client.PostAsync($"/api/v1/payments/{recoveryId}/dispatch", null))
                Assert.Equal(HttpStatusCode.Conflict, recoveryDuplicate.StatusCode);
            using (var beforeReport = app.Services.CreateScope())
                Assert.Equal(3, await beforeReport.ServiceProvider.GetRequiredService<SentinelDbContext>().PaymentTransactionAttempts.CountAsync());
            client.DefaultRequestHeaders.Add("X-Sentinel-Worker-Key", workerKey);
            var reportRoute = $"/api/internal/agent/payments/{recoveryId}/report-transaction";
            (await client.PostAsJsonAsync(reportRoute, new { transactionHash = recoveryHash, operation = "Execution" })).EnsureSuccessStatusCode();
            // A duplicate report must be idempotent, without adding an attempt.
            (await client.PostAsJsonAsync(reportRoute, new { transactionHash = recoveryHash, operation = "Execution" })).EnsureSuccessStatusCode();
            using var scope = app.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<SentinelDbContext>();
            Assert.Equal(2, await db.PaymentRequests.CountAsync());
            Assert.Equal(4, await db.PaymentTransactionAttempts.CountAsync());
            Assert.All(await db.PaymentTransactionAttempts.ToListAsync(), attempt => Assert.Equal("Verified", attempt.Status));
            Assert.True(await db.AuditEvents.AnyAsync(x => x.EventType == "PaymentRiskRejected"));
            var calls = await fixture.GetFromJsonAsync<JsonElement[]>("calls");
            Assert.Contains(calls!, x => x.GetProperty("method").GetString() == "tx");
        }
        finally {
            if (!signer.HasExited) signer.Kill(entireProcessTree: true);
            await signer.WaitForExitAsync();
            await app.StopAsync();
        }
    }
}

