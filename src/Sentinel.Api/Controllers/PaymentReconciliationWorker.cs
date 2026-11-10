
using Microsoft.EntityFrameworkCore;
using Sentinel.Application.Interfaces;
using Sentinel.Infrastructure.Persistence;

namespace Sentinel.Api.BackgroundServices;

public sealed class PaymentReconciliationWorker
    : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<PaymentReconciliationWorker> _logger;

    public PaymentReconciliationWorker(
        IServiceScopeFactory scopeFactory,
        ILogger<PaymentReconciliationWorker> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(
        CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(
            TimeSpan.FromSeconds(30));

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ReconcileAsync(stoppingToken);
            }
            catch (OperationCanceledException)
                when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "Payment reconciliation cycle failed.");
            }

            if (!await timer.WaitForNextTickAsync(stoppingToken))
                break;
        }
    }

    private async Task ReconcileAsync(
        CancellationToken cancellationToken)
    {
        using var scope = _scopeFactory.CreateScope();

        var db = scope.ServiceProvider
            .GetRequiredService<SentinelDbContext>();

        var tracking = scope.ServiceProvider
            .GetRequiredService<IPaymentTrackingService>();

        var contractId = scope.ServiceProvider
            .GetRequiredService<
                Microsoft.Extensions.Options.IOptions<
                    Sentinel.Infrastructure.Near.NearOptions>>()
            .Value.ContractId;


        var recentCutoff = DateTime.UtcNow.AddHours(-24);

        var paymentIds = await db.PaymentRequests
            .AsNoTracking()
            .Where(x =>
                x.ContractId == contractId &&
                (
                    x.Status == "Authorized" ||
                    x.Status == "Executing" ||
                    (x.Status == "Prepared" &&
                     x.CreatedAtUtc >= recentCutoff)
                ))
            .OrderByDescending(x => x.UpdatedAtUtc)
            .Take(50)
            .Select(x => x.PaymentId)
            .ToListAsync(cancellationToken);


        foreach (var paymentId in paymentIds)
        {
            try
            {
                await tracking.SyncAsync(
                    paymentId,
                    cancellationToken);
            }
            catch (OperationCanceledException)
                when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(
                    ex,
                    "Failed to reconcile payment {PaymentId}",
                    paymentId);
            }
        }
    }
}
