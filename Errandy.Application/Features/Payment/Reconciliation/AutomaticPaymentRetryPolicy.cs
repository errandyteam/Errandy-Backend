using System;

namespace Errandy.Application.Features.Payments.Reconciliation;

/// <summary>
/// Calculates a retry time only. This type does not claim work, call Paystack,
/// update a payment, register a worker, or persist retry state.
/// A null result means automatic attempts are exhausted and the payment must
/// remain available for manual review; it does not mean the payment failed.
/// </summary>
public sealed class AutomaticPaymentRetryPolicy
{
    public int MaximumAttempts { get; }
    public TimeSpan MinimumPaymentAge { get; }
    public TimeSpan InitialRetryDelay { get; }
    public TimeSpan MaximumRetryDelay { get; }

    // Conservative provisional defaults, to be reviewed before enabling a worker.
    public AutomaticPaymentRetryPolicy(
        int maximumAttempts = 10,
        TimeSpan? minimumPaymentAge = null,
        TimeSpan? initialRetryDelay = null,
        TimeSpan? maximumRetryDelay = null)
    {
        MaximumAttempts = maximumAttempts;
        MinimumPaymentAge = minimumPaymentAge ?? TimeSpan.FromMinutes(5);
        InitialRetryDelay = initialRetryDelay ?? TimeSpan.FromMinutes(5);
        MaximumRetryDelay = maximumRetryDelay ?? TimeSpan.FromHours(1);

        var maximumAllowedDelay = TimeSpan.FromDays(30);
        if (MaximumAttempts < 1 || MaximumAttempts > 100)
            throw new ArgumentOutOfRangeException(nameof(maximumAttempts));
        if (MinimumPaymentAge <= TimeSpan.Zero || MinimumPaymentAge > maximumAllowedDelay)
            throw new ArgumentOutOfRangeException(nameof(minimumPaymentAge));
        if (InitialRetryDelay <= TimeSpan.Zero || InitialRetryDelay > maximumAllowedDelay)
            throw new ArgumentOutOfRangeException(nameof(initialRetryDelay));
        if (MaximumRetryDelay < InitialRetryDelay || MaximumRetryDelay > maximumAllowedDelay)
            throw new ArgumentOutOfRangeException(nameof(maximumRetryDelay));
    }

    /// <param name="anchorUtc">
    /// For zero attempts, the payment's creation time. After an attempt, its
    /// recorded completion time, or the lease-expiry time after interruption.
    /// The caller is responsible for persisting and supplying the correct anchor.
    /// </param>
    /// <param name="attemptsStarted">Durable count of started attempts, including interrupted attempts.</param>
    /// <returns>UTC time for the next eligible attempt, or null if exhausted.</returns>
    public DateTime? NextAttemptAtUtc(DateTime anchorUtc, int attemptsStarted)
    {
        if (anchorUtc.Kind != DateTimeKind.Utc)
            throw new ArgumentException("The retry anchor must be UTC.", nameof(anchorUtc));
        if (attemptsStarted < 0)
            throw new ArgumentOutOfRangeException(nameof(attemptsStarted));
        if (attemptsStarted >= MaximumAttempts)
            return null;

        if (attemptsStarted == 0)
            return anchorUtc.Add(MinimumPaymentAge);

        // The first started attempt is followed by the initial delay. Double
        // after each subsequent attempt, capped without overflowing ticks.
        var ticks = InitialRetryDelay.Ticks;
        for (var i = 1; i < attemptsStarted && ticks < MaximumRetryDelay.Ticks; i++)
        {
            ticks = ticks > MaximumRetryDelay.Ticks / 2
                ? MaximumRetryDelay.Ticks
                : Math.Min(ticks * 2, MaximumRetryDelay.Ticks);
        }
        return anchorUtc.AddTicks(ticks);
    }
}
