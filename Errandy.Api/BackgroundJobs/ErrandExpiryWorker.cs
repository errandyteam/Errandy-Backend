using Errandy.Application.Features.Errands.ExpireUnacceptedErrands;
using MediatR;

namespace Errandy.Api.BackgroundJobs;

/// <summary>
/// Runs the expiry sweep on a timer. It keeps no state: every sweep reads the
/// deadlines from the database, so restarting the app loses nothing.
/// </summary>
public class ErrandExpiryWorker : BackgroundService
{
    // Gives startup and migrations time to finish before the first sweep.
    private static readonly TimeSpan StartupDelay = TimeSpan.FromSeconds(30);

    // Set this to 1 minute while testing, then back to 5.
    private static readonly TimeSpan SweepInterval = TimeSpan.FromMinutes(5);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<ErrandExpiryWorker> _logger;

    public ErrandExpiryWorker(IServiceScopeFactory scopeFactory, ILogger<ErrandExpiryWorker> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await Task.Delay(StartupDelay, stoppingToken);

            using var timer = new PeriodicTimer(SweepInterval);
            do
            {
                await SweepOnceAsync(stoppingToken);
            }
            while (await timer.WaitForNextTickAsync(stoppingToken));
        }
        catch (OperationCanceledException)
        {
            // The app is shutting down. This is normal.
        }
    }

    private async Task SweepOnceAsync(CancellationToken stoppingToken)
    {
        try
        {
            // The worker lives for the whole app lifetime, but MediatR and the
            // DbContext are per-request. Each sweep gets its own short-lived scope.
            using var scope = _scopeFactory.CreateScope();
            var mediator = scope.ServiceProvider.GetRequiredService<IMediator>();
            await mediator.Send(new ExpireUnacceptedErrandsCommand(), stoppingToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // One failed sweep must never kill the worker.
            _logger.LogError(ex, "Errand expiry sweep failed.");
        }
    }
}