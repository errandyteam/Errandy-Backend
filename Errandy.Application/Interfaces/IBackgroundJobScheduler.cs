namespace Errandy.Application.Interfaces;

/// <summary>
/// Contract for scheduling delayed work. Real implementation should use
/// Hangfire (needs persistent storage — SQL Server/Postgres — so it's
/// blocked on whichever DB the team settles on). Until then, a lightweight
/// in-process fake lets you test the auto-release flow locally.
/// </summary>
public interface IBackgroundJobScheduler
{
    void ScheduleAutoReleaseErrand(Guid errandId, TimeSpan delay);
}