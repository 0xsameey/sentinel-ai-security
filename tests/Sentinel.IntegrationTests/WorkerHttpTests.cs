using Sentinel.Application.Security;
using Sentinel.Application.DTOs;
using Microsoft.AspNetCore.RateLimiting;
using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Hosting;
using Sentinel.Api.BackgroundServices;
using Sentinel.Api.Authentication;
using Sentinel.Api.Controllers;
using Sentinel.Application.Interfaces;
using Sentinel.Domain.Entities;
using Sentinel.Domain.Enums;
using Sentinel.Infrastructure.Near;

namespace Sentinel.IntegrationTests;

// Executes real MVC routing/serialization, worker-key authentication and payment
// execution preparation over TCP, then runs the real TS executor as a child.
// Chain state and persistence are explicit test doubles; no NEAR settlement claim.
public sealed class WorkerHttpTests
{
    [Fact]
    public async Task Production_host_routes_and_authentication_start_without_using_database()
    {
        var args = new[] {
            "--environment=Testing", "--urls=http://127.0.0.1:0",
            "--ConnectionStrings:SentinelDb=Host=127.0.0.1;Port=1;Database=unused;Username=unused",
            "--Near:RpcUrl=https://rpc.testnet.invalid", "--Near:ContractId=treasury.testnet",
            "--Auth:Issuer=sentinel-tests", "--Auth:Audience=sentinel-tests", "--Auth:Recipient=sentinel-tests",
            "--Auth:AccessTokenMinutes=5", $"--Auth:JwtSigningKey={Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))}",
            $"--AgentWorker:ApiKey={Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))}"
        };
        await using var app = SentinelHost.Build(args, builder => {
            foreach (var descriptor in builder.Services.Where(d => d.ServiceType == typeof(IHostedService) &&
                d.ImplementationType == typeof(PaymentReconciliationWorker)).ToArray()) builder.Services.Remove(descriptor);
            builder.Services.AddSingleton<INearContractService>(new State());
            builder.Logging.ClearProviders();
        });
        await app.StartAsync();
        try {
            var address = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single();
            using var client = new HttpClient { BaseAddress = new Uri(address) };
            var config = await client.GetFromJsonAsync<ContractConfigDto>("/api/v1/contract/config");
            Assert.Equal("owner.testnet", config!.Owner);
            using var denied = await client.PostAsJsonAsync("/api/v1/payments/prepare", new { });
            Assert.Equal(HttpStatusCode.Unauthorized, denied.StatusCode);
        } finally { await app.StopAsync(); }
    }

    [Fact]
    public async Task Manual_testing_blocks_all_dispatch_and_internal_signer_routes_without_database_access()
    {
        var args = new[] {
            "--environment=Testing", "--urls=http://127.0.0.1:0",
            "--ConnectionStrings:SentinelDb=Host=127.0.0.1;Port=1;Database=unused;Username=unused",
            "--Near:RpcUrl=https://rpc.testnet.invalid", "--Near:ContractId=treasury.testnet",
            "--Auth:Issuer=sentinel-tests", "--Auth:Audience=sentinel-tests", "--Auth:Recipient=sentinel-tests",
            "--Auth:AccessTokenMinutes=5", $"--Auth:JwtSigningKey={Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))}",
            "--Safety:DisablePaymentDispatch=true", "--Workers:ReconciliationEnabled=false"
        };
        await using var app = SentinelHost.Build(args, b => {
            b.Services.AddSingleton<INearContractService>(new State());
            b.Logging.ClearProviders();
        });
        Assert.DoesNotContain(app.Services.GetServices<IHostedService>(), x => x is PaymentReconciliationWorker);
        await app.StartAsync();
        try {
            var address=app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single();
            using var client=new HttpClient{BaseAddress=new Uri(address)};
            foreach(var path in new[]{"/api/v1/payments/p/dispatch","/api/v1/delegated-agent/payments/p/dispatch/",
                "/api/internal/agent/payments/p/prepare-execution","/api/internal/agent/delegated-payments/p/consume-permit",
                "/api/v1/delegated-agent/tasks/","/API/V1/DELEGATED-AGENT/TASKS"}) {
                using var response=await client.PostAsJsonAsync(path,new{});
                Assert.Equal(HttpStatusCode.ServiceUnavailable,response.StatusCode);
                Assert.Contains("PAYMENT_DISPATCH_DISABLED_MANUAL_TESTING",await response.Content.ReadAsStringAsync());
            }
            Assert.Equal("owner.testnet",(await client.GetFromJsonAsync<ContractConfigDto>("/api/v1/contract/config"))!.Owner);
            using var unauthorized=await client.GetAsync("/api/v1/auth/me");
            Assert.Equal(HttpStatusCode.Unauthorized,unauthorized.StatusCode);
        } finally {await app.StopAsync();}
    }

    [Fact]
    public async Task TypeScript_worker_consumes_API_call_and_reports_execution_over_HTTP()
    {
        var key = Convert.ToBase64String(RandomNumberGenerator.GetBytes(48));
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Testing" });
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Logging.ClearProviders();
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?> { ["AgentWorker:ApiKey"] = key });
        builder.Services.AddControllers().AddApplicationPart(typeof(AgentWorkerController).Assembly);
        builder.Services.AddSingleton<AgentWorkerAuthenticator>();
        var state = new State();
        builder.Services.AddSingleton<INearContractService>(state);
        builder.Services.AddSingleton<ISpendingPolicyService>(state);
        builder.Services.AddSingleton<IPaymentTrackingService>(state);
        builder.Services.AddSingleton(Options.Create(new NearOptions { ContractId = "treasury.testnet", RpcUrl = "https://rpc.testnet.invalid" }));
        builder.Services.AddSingleton<ISecurityGateway>(new HttpContractGateway());
        builder.Services.AddSingleton<IPaymentTransactionService, PaymentTransactionService>();
        builder.Services.AddRateLimiter(o => o.AddFixedWindowLimiter("worker", x => { x.PermitLimit = 100; x.Window = TimeSpan.FromMinutes(1); }));
        await using var app = builder.Build();
        app.UseRateLimiter();
        app.MapControllers();
        await app.StartAsync();
        try {
            var address = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single();
            using var client = new HttpClient { BaseAddress = new Uri(address) };
            var route = "/api/internal/agent/payments/p1";
            using var unauthenticated = await client.PostAsJsonAsync(route + "/prepare-execution", new { });
            Assert.Equal(HttpStatusCode.Unauthorized, unauthenticated.StatusCode);
            client.DefaultRequestHeaders.Add("X-Sentinel-Worker-Key", key);
            using var wrongOperation = await client.PostAsJsonAsync(route + "/report-transaction", new { transactionHash = "hash", operation = "Authorization" });
            Assert.Equal(HttpStatusCode.BadRequest, wrongOperation.StatusCode);
            state.Paused = true;
            using var paused = await client.PostAsJsonAsync(route + "/prepare-execution", new { });
            Assert.Equal(HttpStatusCode.Conflict, paused.StatusCode);
            state.Paused = false;
            var root = FindRoot();
            // Build via the reproducible script or npm --prefix src/sentinel-signer run build.
            var worker = Path.Combine(root, "src/sentinel-signer/dist/tests/execute-http-fixture.js");
            Assert.True(File.Exists(worker), "Build signer first: npm.cmd --prefix src/sentinel-signer run build");
            var start = new ProcessStartInfo("node") { UseShellExecute = false, CreateNoWindow = true,
                RedirectStandardOutput = true, RedirectStandardError = true, WorkingDirectory = root };
            start.ArgumentList.Add(worker); start.ArgumentList.Add("p1");
            start.Environment["TEST_API_URL"] = address;
            start.Environment["TEST_WORKER_KEY"] = key;
            using var process = Process.Start(start)!;
            try {
                await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(20));
                Assert.Equal(0, process.ExitCode);
                Assert.Equal("synthetic-execution-hash", (await process.StandardOutput.ReadToEndAsync()).Trim());
            } finally { if (!process.HasExited) process.Kill(entireProcessTree: true); }
            Assert.Equal("Execution", state.Report?.Operation);
            Assert.Equal("synthetic-execution-hash", state.Report?.TransactionHash);
            Assert.Equal("agent.testnet", state.ReportingAccount);
        } finally { await app.StopAsync(); }
    }

    private static string FindRoot() {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null && !File.Exists(Path.Combine(current.FullName, "Sentinel.slnx"))) current = current.Parent;
        return current?.FullName ?? throw new InvalidOperationException("Repository root not found.");
    }

    private sealed class HttpContractGateway : ISecurityGateway
    {
        private readonly Guid permit = Guid.NewGuid();
        private readonly DateTimeOffset expires = DateTimeOffset.UtcNow.AddMinutes(1);
        private ContractCallDto Call(string id, bool secured) => new("treasury.testnet", "execute_payment", new { id }, "30000000000000", "0",
            secured ? new ExecutionSecurityDto(permit, new string('a',64), expires, "ed25519:test-signer") : null);
        public Task<ContractCallDto> PrepareExecutionAsync(string actor, string id, CancellationToken ct=default) => Task.FromResult(Call(id,true));
        public Task<ConsumedPermitDto> ConsumeAsync(string id, ConsumePermitRequest request, CancellationToken ct=default) =>
            Task.FromResult(new ConsumedPermitDto(permit,id,new string('a',64),"ALLOW",false,expires,"ed25519:test-signer",Call(id,false)));
        public Task<GatewayAssessmentDto> AssessAsync(string actor,GatewayPaymentRequest request,string stage="Authorization",string? paymentId=null,CancellationToken ct=default)=>throw new NotSupportedException();
        public Task<GatewayAssessmentDto> GetAsync(string actor,Guid id,CancellationToken ct=default)=>throw new NotSupportedException();
        public Task<HumanApprovalDto> ApproveAsync(string actor,Guid id,ApproveAssessmentRequest request,CancellationToken ct=default)=>throw new NotSupportedException();
        public Task<GatewayAssessmentDto> AuthorizePreparationAsync(string owner,GatewayPaymentRequest request,Guid? assessmentId,Guid? approvalId,string paymentId,CancellationToken ct=default)=>throw new NotSupportedException();
        public Task PreflightExecutionAsync(string actor,string paymentId,CancellationToken ct=default)=>throw new NotSupportedException();
    }

    private sealed class State : INearContractService, ISpendingPolicyService, IPaymentTrackingService
    {
        public bool Paused { get; set; }
        public ReportTransactionRequest? Report { get; private set; }
        public string? ReportingAccount { get; private set; }
        public Task<ContractConfigDto> GetConfigAsync(CancellationToken ct = default) => Task.FromResult(new ContractConfigDto("owner.testnet", "agent.testnet", Paused));
        public Task<string> GetAvailableBalanceAsync(CancellationToken ct = default) => Task.FromResult("10000");
        public Task<Payment?> GetPaymentAsync(string id, CancellationToken ct = default) => Task.FromResult<Payment?>(new(id, "merchant.testnet", "100", checked((ulong)DateTimeOffset.UtcNow.AddHours(1).ToUnixTimeMilliseconds() * 1_000_000UL), PaymentStatus.Authorized));
        public Task<SpendingPolicyDto> GetPolicyAsync(CancellationToken ct = default) => Task.FromResult(new SpendingPolicyDto("1000", "10000"));
        public Task<DailyUsageDto> GetDailyUsageAsync(CancellationToken ct = default) => Task.FromResult(new DailyUsageDto(1, "0", "0", "0"));
        public Task<RecipientStatusDto> GetRecipientStatusAsync(string id, CancellationToken ct = default) => Task.FromResult(new RecipientStatusDto(id, true));
        public Task<TransactionVerificationDto> ReportTransactionAsync(string id, string account, ReportTransactionRequest report, CancellationToken ct = default) {
            Report = report; ReportingAccount = account;
            return Task.FromResult(new TransactionVerificationDto(report.TransactionHash, id, report.Operation, "Verified"));
        }
        public Task<PreparedPaymentDto> PrepareAsync(string owner, string key, AuthorizePaymentRequest request, CancellationToken ct = default, Guid? assessmentId = null, Guid? approvalId = null) => throw new NotSupportedException();
        public Task<PaymentTrackingDto?> GetAsync(string id, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<PaymentTrackingDto?> SyncAsync(string id, CancellationToken ct = default) => throw new NotSupportedException();
    }
}
