using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Errandy.Api.BackgroundServices;

/// <summary>
/// Temporarily inert. A hold expiring does not prove a bank payout occurred.
/// Replace only when confirmed payout handling and reconciliation are ready.
/// </summary>
public class WithdrawalHoldBackgroundService : BackgroundService
{
    private readonly ILogger<WithdrawalHoldBackgroundService> _logger;

    // Keep the existing constructor signature for compatibility with callers.
    public WithdrawalHoldBackgroundService(
        IServiceScopeFactory scopeFactory,
        ILogger<WithdrawalHoldBackgroundService> logger)
    {
        _ = scopeFactory;
        _logger = logger;
    }

    protected override Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogWarning("Automatic withdrawal completion is disabled until confirmed payout handling is available.");
        return Task.CompletedTask;
    }
}
