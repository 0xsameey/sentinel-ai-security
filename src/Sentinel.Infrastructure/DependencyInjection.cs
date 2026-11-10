
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Sentinel.Application.Interfaces;
using Sentinel.Infrastructure.AI;
using Sentinel.Infrastructure.Authentication;
using Sentinel.Infrastructure.Near;
using Sentinel.Infrastructure.Persistence;
using Sentinel.Application.Security;
using Sentinel.Infrastructure.Security;

namespace Sentinel.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddOptions<NearOptions>()
            .Bind(configuration.GetSection(
                NearOptions.SectionName))
            .ValidateDataAnnotations()
            .Validate(
                options =>
                    Uri.TryCreate(
                        options.RpcUrl,
                        UriKind.Absolute,
                        out var uri) &&
                    uri.Scheme == Uri.UriSchemeHttps,
                "NEAR RPC URL must use HTTPS.")
            .ValidateOnStart();

        var connectionString = configuration
            .GetConnectionString("SentinelDb");

        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException(
                "SentinelDb connection string is missing.");
        }

        services.AddDbContext<SentinelDbContext>(
            options => options.UseNpgsql(connectionString));

        services.AddHttpClient<NearRpcClient>((sp, client) =>
        {
            var nearOptions = sp
                .GetRequiredService<IOptions<NearOptions>>()
                .Value;

            client.BaseAddress =
                new Uri(nearOptions.RpcUrl);

            client.Timeout = TimeSpan.FromSeconds(20);
        });

        services.AddScoped<
            INearContractService,
            NearContractService>();

                services.AddScoped<
            IAuthChallengeService,
            AuthChallengeService>();

        services.AddHttpClient<NearAccessKeyService>((sp, client) =>
        {
            var options = sp.GetRequiredService<
                Microsoft.Extensions.Options.IOptions<NearOptions>>().Value;

            client.BaseAddress = new Uri(options.RpcUrl);
            client.Timeout = TimeSpan.FromSeconds(20);
        });

        services.AddSingleton<Nep413Verifier>();

        services.AddScoped<
    IWalletVerificationService,
    WalletVerificationService>();

        services.AddScoped<
    ISpendingPolicyService,
    SpendingPolicyService>();

        services.AddScoped<
    IPolicyTransactionService,
    PolicyTransactionService>();

        services.AddScoped<
    IPaymentTransactionService,
    PaymentTransactionService>();
        services.AddScoped<
    IPaymentTrackingService,
    PaymentTrackingService>();

        services.AddHttpClient<NearTransactionVerifier>(
            (sp, client) =>
            {
                var options = sp.GetRequiredService<
                    Microsoft.Extensions.Options.IOptions<NearOptions>>()
                    .Value;

                client.BaseAddress = new Uri(options.RpcUrl);
                client.Timeout = TimeSpan.FromSeconds(20);
            });


        services.AddSingleton(TimeProvider.System);
        services.AddSingleton<DeterministicRiskEngine>();
        services.AddHttpClient<NearSecurityRpc>((sp, client) => {
            client.BaseAddress = new Uri(sp.GetRequiredService<IOptions<NearOptions>>().Value.RpcUrl);
            client.Timeout = TimeSpan.FromSeconds(10);
            client.MaxResponseContentBufferSize = 1_000_000;
        });
        services.AddHttpClient<ICounterpartyHistory, NearblocksHistory>(client => {
            client.Timeout = TimeSpan.FromSeconds(5);
            client.MaxResponseContentBufferSize = 1_000_000;
        });
        services.AddScoped<ISecurityFactsProvider, NearSecurityFactsProvider>();
        services.AddScoped<IExecutionReleaseGate, NearExecutionReleaseGate>();
        services.AddScoped<ISecurityGateway, SecurityGateway>();
        services.AddScoped<IDelegationReader, NearDelegationReader>();
        services.AddScoped<IDelegatedReleaseGate, NearDelegatedReleaseGate>();
        services.AddScoped<DelegatedSpendingService>();

        services.AddHttpClient<
            IAiExplanationService,
            OpenAiExplanationService>(client =>
            {
                client.Timeout = TimeSpan.FromSeconds(12);
            });

        return services;
    }
}

