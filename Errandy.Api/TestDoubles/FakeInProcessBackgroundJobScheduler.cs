using Errandy.Application.Features.Errands.AutoReleaseErrand;
using Errandy.Application.Interfaces;
using MediatR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Errandy.Api.TestDoubles;

/// <summary>
/// TEMPORARY — in-process only, resets on restart. Real version needs
/// Hangfire + persistent storage once the team picks a DB.
/// </summary>
public class FakeInProcessBackgroundJobScheduler : IBackgroundJobScheduler
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<FakeInProcessBackgroundJobScheduler> _logger;

    public FakeInProcessBackgroundJobScheduler(
        IServiceScopeFactory scopeFactory,
        ILogger<FakeInProcessBackgroundJobScheduler> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    public void ScheduleAutoReleaseErrand(Guid errandId, TimeSpan delay)
    {
        _logger.LogInformation(
            "[FAKE SCHEDULER] Auto-release for errand {ErrandId} scheduled in {Delay}.",
            errandId, delay);

        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(delay);
                using var scope = _scopeFactory.CreateScope();
                var mediator = scope.ServiceProvider.GetRequiredService<IMediator>();
                await mediator.Send(new AutoReleaseErrandCommand { ErrandId = errandId });
                _logger.LogInformation("[FAKE SCHEDULER] Auto-release fired for errand {ErrandId}.", errandId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[FAKE SCHEDULER] Auto-release failed for errand {ErrandId}.", errandId);
            }
        });
    }
}