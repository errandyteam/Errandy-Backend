using Errandy.Application.Interfaces;
using Errandy.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using WalletTransactionStatus = Errandy.Domain.Enums.TransactionStatus;

namespace Errandy.Api.BackgroundServices;

/// <summary>
/// Automatically sweeps and completes pending withdrawals whose 24-hour security hold
/// (EligibleAt <= UtcNow) has elapsed.
/// Register in Program.cs via: builder.Services.AddHostedService<WithdrawalHoldBackgroundService>();
/// </summary>
public class WithdrawalHoldBackgroundService : BackgroundService
{
    private static readonly TimeSpan SweepInterval = TimeSpan.FromMinutes(5);
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<WithdrawalHoldBackgroundService> _logger;

    public WithdrawalHoldBackgroundService(
        IServiceScopeFactory scopeFactory,
        ILogger<WithdrawalHoldBackgroundService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Delay first sweep slightly so startup/migrations finish cleanly
        await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await SweepMaturedWithdrawalsAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "[WITHDRAWAL SWEEPER] Error while sweeping matured withdrawals.");
            }

            await Task.Delay(SweepInterval, stoppingToken);
        }
    }

    private async Task SweepMaturedWithdrawalsAsync(CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IApplicationDbContext>();
        var dateTimeProvider = scope.ServiceProvider.GetRequiredService<IDateTimeProvider>();

        var utcNow = dateTimeProvider.UtcNow;

        var matured = await db.WithdrawalRequests
            .Where(w => w.Status == WithdrawalStatus.PendingHold && w.EligibleAt <= utcNow)
            .ToListAsync(ct);

        if (matured.Count == 0)
            return;

        var txIds = matured.Select(w => w.TransactionId).ToList();
        var transactions = await db.Transactions
            .Where(t => txIds.Contains(t.Id))
            .ToDictionaryAsync(t => t.Id, ct);

        foreach (var withdrawal in matured)
        {
            withdrawal.Complete(utcNow, null, "Auto-settled after 24-hour security hold.");
            if (transactions.TryGetValue(withdrawal.TransactionId, out var tx))
            {
                tx.Status = WalletTransactionStatus.Completed;
            }
        }

        await db.SaveChangesAsync(ct);

        _logger.LogInformation(
            "[WITHDRAWAL SWEEPER] Auto-completed {Count} matured withdrawal(s) at {UtcNow:O}.",
            matured.Count, utcNow);
    }
}
