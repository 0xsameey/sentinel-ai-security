
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.IdentityModel.Tokens;
using Sentinel.Api.Authentication;
using Sentinel.Api.BackgroundServices;
using Sentinel.Api.Exceptions;
using Sentinel.Infrastructure;
using Sentinel.Infrastructure.Persistence;
using Serilog;
using System.IdentityModel.Tokens.Jwt;
using System.Threading.RateLimiting;

Log.Logger = new LoggerConfiguration().WriteTo.Console().CreateBootstrapLogger();
try { SentinelHost.Build(args).Run(); }
catch (Exception ex) { Log.Fatal(ex, "Sentinel API failed to start"); throw; }
finally { Log.CloseAndFlush(); }

// Test hosts use the production pipeline with isolated configuration/dependencies.
public static class SentinelHost
{
    public static WebApplication Build(string[] args, Action<WebApplicationBuilder>? configure = null)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            Args = args,
            ApplicationName = typeof(SentinelHost).Assembly.GetName().Name
        });

        builder.Host.UseSerilog((context, services, config) =>
        {
            config
                .ReadFrom.Services(services)
                .MinimumLevel.Information()
                .MinimumLevel.Override(
                    "Microsoft.AspNetCore", Serilog.Events.LogEventLevel.Warning)
                .Enrich.FromLogContext()
                .WriteTo.Console();
        });

        builder.Services.AddControllers();
        builder.Services.AddEndpointsApiExplorer();
        builder.Services.AddSwaggerGen();

        builder.Services.AddInfrastructure(builder.Configuration);
        if (builder.Configuration.GetValue<bool>("Safety:DisablePaymentDispatch"))
            Sentinel.Api.ManualTestingProtection.Configure(builder.Services);

        builder.Services.AddExceptionHandler<GlobalExceptionHandler>();
        builder.Services.AddProblemDetails();

        builder.Services.AddHealthChecks()
            .AddCheck<DatabaseHealthCheck>("database");


        var authSection = builder.Configuration.GetSection("Auth");

        var signingKeyBase64 = authSection["JwtSigningKey"]
            ?? throw new InvalidOperationException(
                "JWT signing key is missing.");

        byte[] signingKey;

        try
        {
            signingKey = Convert.FromBase64String(
                signingKeyBase64);
        }
        catch (FormatException ex)
        {
            throw new InvalidOperationException(
                "JWT signing key must be Base64.", ex);
        }

        if (signingKey.Length < 32)
            throw new InvalidOperationException(
                "JWT signing key must be at least 32 bytes.");

        var issuer = authSection["Issuer"];
        var audience = authSection["Audience"];
        var lifetime = authSection.GetValue<int>("AccessTokenMinutes");

        if (string.IsNullOrWhiteSpace(issuer) ||
            string.IsNullOrWhiteSpace(audience) ||
            lifetime is < 1 or > 60)
        {
            throw new InvalidOperationException(
                "Invalid JWT authentication configuration.");
        }

        builder.Services.AddControllers()
        .AddJsonOptions(options =>
        {
            options.JsonSerializerOptions.Converters.Add(
                new System.Text.Json.Serialization.JsonStringEnumConverter()
            );
        });
        builder.Services.AddSingleton<JwtTokenService>();

        builder.Services
            .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(options =>
            {
                options.MapInboundClaims = false;

                options.TokenValidationParameters = new()
                {
                    ValidateIssuer = true,
                    ValidIssuer = issuer,

                    ValidateAudience = true,
                    ValidAudience = audience,

                    ValidateIssuerSigningKey = true,
                    IssuerSigningKey =
                        new SymmetricSecurityKey(signingKey),

                    ValidateLifetime = true,
                    RequireExpirationTime = true,
                    RequireSignedTokens = true,

                    ClockSkew = TimeSpan.FromSeconds(30),

                    NameClaimType = JwtRegisteredClaimNames.Sub
                };
            })
            .AddScheme<Microsoft.AspNetCore.Authentication.AuthenticationSchemeOptions, GatewayAgentHandler>(GatewayAgentHandler.SchemeName, _ => { });
        if (builder.Configuration.GetValue<bool?>("Workers:ReconciliationEnabled") ?? true)
            builder.Services.AddHostedService<PaymentReconciliationWorker>();

        builder.Services.AddAuthorization();


        builder.Services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode =
                StatusCodes.Status429TooManyRequests;

            options.AddPolicy("auth", context =>
                RateLimitPartition.GetFixedWindowLimiter(
                    partitionKey:
                        context.Connection.RemoteIpAddress?.ToString()
                        ?? "unknown",
                    factory: _ => new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = 10,
                        Window = TimeSpan.FromMinutes(1),
                        QueueLimit = 0,
                        AutoReplenishment = true
                    }));
            foreach (var name in new[] { "gateway", "worker" })
                options.AddPolicy(name, context => RateLimitPartition.GetFixedWindowLimiter(
                    context.Connection.RemoteIpAddress?.ToString() ?? "unknown", _ => new FixedWindowRateLimiterOptions {
                        PermitLimit = name == "gateway" ? Math.Clamp(builder.Configuration.GetValue<int?>("RateLimits:GatewayPermitLimit") ?? 30, 1, 300) : 120,
                        Window = TimeSpan.FromMinutes(1), QueueLimit = 0, AutoReplenishment = true
                    }));
        });
        builder.Services.AddSingleton<AgentWorkerAuthenticator>();


        builder.Services.AddCors(options =>
        {
            options.AddPolicy("SentinelDashboard", policy =>
            {
                policy.WithOrigins("http://localhost:5173")
                    .AllowAnyHeader()
                    .AllowAnyMethod();
            });
        });
        builder.Services.AddHttpClient(
        "SentinelAgentDispatcher",
        client =>
        {
            client.Timeout = TimeSpan.FromSeconds(60);
        }).ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler { AllowAutoRedirect = false });
        builder.Services.AddHttpClient("SentinelAgentRunner", client => {
            client.Timeout = TimeSpan.FromSeconds(100);
            client.MaxResponseContentBufferSize = 64_000;
        }).ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler { AllowAutoRedirect = false });

        configure?.Invoke(builder);
        var app = builder.Build();

        app.UseSerilogRequestLogging();

        app.UseExceptionHandler();

        if (app.Environment.IsDevelopment())
        {
            app.UseSwagger();
            app.UseSwaggerUI();
        }

        app.UseHttpsRedirection();
        app.UseCors("SentinelDashboard");
        if (builder.Configuration.GetValue<bool>("Safety:DisablePaymentDispatch"))
        {
            app.Use(async (context, next) =>
            {
                var path = context.Request.Path;
                if (path.StartsWithSegments("/api/internal/agent", StringComparison.OrdinalIgnoreCase) ||
                    (path.Value?.TrimEnd('/').EndsWith("/dispatch", StringComparison.OrdinalIgnoreCase) ?? false) ||
                    (path.Value?.TrimEnd('/').Equals("/api/v1/delegated-agent/tasks", StringComparison.OrdinalIgnoreCase) ?? false))
                {
                    context.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
                    await context.Response.WriteAsJsonAsync(new { code = "PAYMENT_DISPATCH_DISABLED_MANUAL_TESTING" });
                    return;
                }
                await next(context);
            });
        }
        app.UseRateLimiter();
        app.UseAuthentication();
        app.UseAuthorization();
        app.MapControllers();
        app.MapHealthChecks("/health");

        return app;
    }
}

public sealed class DatabaseHealthCheck : IHealthCheck
{
    private readonly SentinelDbContext _db;

    public DatabaseHealthCheck(SentinelDbContext db)
    {
        _db = db;
    }

    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var connected = await _db.Database
                .CanConnectAsync(cancellationToken);

            return connected
                ? HealthCheckResult.Healthy()
                : HealthCheckResult.Unhealthy(
                    "Database connection unavailable.");
        }
        catch (Exception ex)
        {
            return HealthCheckResult.Unhealthy(
                "Database health check failed.", ex);
        }
    }
}

