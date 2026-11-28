using Errandy.Application.Features.Errands.AutoReleaseErrand;
using Errandy.Application.Features.Errands.GetErrandsDueForAutoRelease;
using MediatR;

namespace Errandy.Api.BackgroundJobs;

/// <summary>
/// Releases payment for errands whose customer-confirmation window has passed. It keeps no
/// state: every sweep reads the due times from the database, so restarting the app loses nothing.
/// </summary>
public class ErrandAutoReleaseWorker : BackgroundService
{
    // Gives startup and migrations time to finish before the first sweep.
    private static readonly TimeSpan StartupDelay = TimeSpan.FromSeconds(30);

    // Releases can be up to this much later than the exact deadline.
    // Set this to 1 minute while testing, then back to 5.
    private static readonly TimeSpan SweepInterval = TimeSpan.FromMinutes(5);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<ErrandAutoReleaseWorker> _logger;

    public ErrandAutoReleaseWorker(IServiceScopeFactory scopeFactory, ILogger<ErrandAutoReleaseWorker> logger)
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
        List<Guid> dueErrandIds;

        try
        {
            using var scope = _scopeFactory.CreateScope();
            var mediator = scope.ServiceProvider.GetRequiredService<IMediator>();
            dueErrandIds = await mediator.Send(new GetErrandsDueForAutoReleaseQuery(), stoppingToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Could not look up errands due for auto-release.");
            return;
        }

        foreach (var errandId in dueErrandIds)
        {
            stoppingToken.ThrowIfCancellationRequested();

            try
            {
                // Its own scope per errand, so one failure can't affect the others.
                using var scope = _scopeFactory.CreateScope();
                var mediator = scope.ServiceProvider.GetRequiredService<IMediator>();
                await mediator.Send(new AutoReleaseErrandCommand { ErrandId = errandId }, stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError(ex, "Auto-release failed for errand {ErrandId}.", errandId);
            }
        }
    }
}