namespace Errandy.Application.Interfaces;

/// <summary>
/// Thin abstraction over the system clock. Handlers depend on this instead of
/// calling DateTime.UtcNow directly, so tests can inject a fixed time.
/// Implement in Infrastructure as a one-liner:
///   public class DateTimeProvider : IDateTimeProvider { public DateTime UtcNow => DateTime.UtcNow; }
/// </summary>
public interface IDateTimeProvider
{
    DateTime UtcNow { get; }
}
