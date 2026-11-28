using System;
using Errandy.Domain.Common;
using Errandy.Domain.Enums;

namespace Errandy.Domain.Entities;

/// <summary>
/// Proposed durable scheduling state for one payment. This type alone does not
/// persist anything or safely coordinate multiple workers. A later storage layer
/// MUST enforce unique PaymentIntentId and atomic transitions using Revision as
/// a concurrency token (or equivalent database-conditional updates).
/// A lease fences scheduling updates only; it cannot stop an expired worker's
/// gateway request or wallet mutation. The verifier must remain idempotent.
/// </summary>
public class AutomaticPaymentCheck : BaseEntity
{
    public Guid PaymentIntentId { get; private set; }
    public AutomaticPaymentCheckStatus Status { get; private set; }
    public AutomaticPaymentCheckResult LastResult { get; private set; }
    public int AttemptsStarted { get; private set; }
    public int MaximumAttempts { get; private set; }
    public DateTime? NextAttemptAt { get; private set; }
    public Guid? LeaseToken { get; private set; }
    public DateTime? LeaseExpiresAt { get; private set; }
    public DateTime? LastStartedAt { get; private set; }
    public DateTime? LastFinishedAt { get; private set; }
    public long Revision { get; private set; }

    private AutomaticPaymentCheck() { }

    public static AutomaticPaymentCheck Schedule(Guid paymentIntentId, DateTime firstAttemptAtUtc, int maximumAttempts)
    {
        if (paymentIntentId == Guid.Empty)
            throw new ArgumentException("Payment intent ID is required.", nameof(paymentIntentId));
        RequireUtc(firstAttemptAtUtc, nameof(firstAttemptAtUtc));
        if (maximumAttempts < 1 || maximumAttempts > 100)
            throw new ArgumentOutOfRangeException(nameof(maximumAttempts));
        return new AutomaticPaymentCheck
        {
            PaymentIntentId = paymentIntentId,
            Status = AutomaticPaymentCheckStatus.Scheduled,
            LastResult = AutomaticPaymentCheckResult.None,
            NextAttemptAt = firstAttemptAtUtc,
            MaximumAttempts = maximumAttempts
        };
    }

    // The caller must save this change atomically before invoking the verifier.
    // A unique, unpredictable token must be generated for EACH lease.
    public bool TryStart(Guid leaseToken, DateTime nowUtc, TimeSpan leaseDuration)
    {
        if (leaseToken == Guid.Empty)
            throw new ArgumentException("A nonempty lease token is required.", nameof(leaseToken));
        RequireUtc(nowUtc, nameof(nowUtc));
        if (leaseDuration <= TimeSpan.Zero || leaseDuration > TimeSpan.FromHours(24))
            throw new ArgumentOutOfRangeException(nameof(leaseDuration));
        if (Status != AutomaticPaymentCheckStatus.Scheduled || NextAttemptAt is null || NextAttemptAt > nowUtc)
            return false;
        if (AttemptsStarted >= MaximumAttempts)
            throw new InvalidOperationException("The automatic attempt limit has been reached.");

        // Compute before mutating state, including the DateTime overflow check.
        var expires = nowUtc.Add(leaseDuration);
        Revision = checked(Revision + 1);
        AttemptsStarted++;
        Status = AutomaticPaymentCheckStatus.Leased;
        NextAttemptAt = null;
        LeaseToken = leaseToken;
        LeaseExpiresAt = expires;
        LastStartedAt = nowUtc;
        UpdatedAt = nowUtc;
        return true;
    }

    // Only use after the shared verifier returns a successful, non-null result.
    public void MarkConfirmed(Guid leaseToken, DateTime nowUtc)
    {
        RequireLiveLease(leaseToken, nowUtc);
        Revision = checked(Revision + 1);
        Status = AutomaticPaymentCheckStatus.Confirmed;
        LastResult = AutomaticPaymentCheckResult.Confirmed;
        LastFinishedAt = nowUtc;
        NextAttemptAt = null;
        ClearLease(nowUtc);
    }

    // An unsuccessful generic result cannot prove the payment was not credited.
    // If attempts remain, nextAttemptAtUtc must be strictly in the future. When
    // exhausted, it must be null and the record becomes visible for review.
    public void MarkUnconfirmed(Guid leaseToken, DateTime nowUtc, DateTime? nextAttemptAtUtc)
    {
        RequireLiveLease(leaseToken, nowUtc);
        ValidateNextAttempt(nextAttemptAtUtc, nowUtc, requireStrictlyAfter: true);
        Revision = checked(Revision + 1);
        Status = nextAttemptAtUtc.HasValue ? AutomaticPaymentCheckStatus.Scheduled : AutomaticPaymentCheckStatus.NeedsReview;
        LastResult = AutomaticPaymentCheckResult.Unconfirmed;
        LastFinishedAt = nowUtc;
        NextAttemptAt = nextAttemptAtUtc;
        ClearLease(nowUtc);
    }

    // An expired lease counts as an attempt. The retry time may already be due
    // when recovery runs late, but it must not precede the lease's expiry.
    public void RecoverExpiredLease(DateTime nowUtc, DateTime? nextAttemptAtUtc)
    {
        RequireUtc(nowUtc, nameof(nowUtc));
        if (Status != AutomaticPaymentCheckStatus.Leased || LeaseExpiresAt is null || nowUtc < LeaseExpiresAt.Value)
            throw new InvalidOperationException("There is no expired lease to recover.");
        ValidateNextAttempt(nextAttemptAtUtc, LeaseExpiresAt.Value, requireStrictlyAfter: false);
        Revision = checked(Revision + 1);
        Status = nextAttemptAtUtc.HasValue ? AutomaticPaymentCheckStatus.Scheduled : AutomaticPaymentCheckStatus.NeedsReview;
        LastResult = AutomaticPaymentCheckResult.LeaseExpired;
        LastFinishedAt = nowUtc;
        NextAttemptAt = nextAttemptAtUtc;
        ClearLease(nowUtc);
    }

    private void RequireLiveLease(Guid token, DateTime nowUtc)
    {
        RequireUtc(nowUtc, nameof(nowUtc));
        if (token == Guid.Empty || Status != AutomaticPaymentCheckStatus.Leased || LeaseToken != token ||
            LeaseExpiresAt is null || LastStartedAt is null || nowUtc < LastStartedAt.Value || nowUtc >= LeaseExpiresAt.Value)
            throw new InvalidOperationException("The lease is missing, expired, or no longer owned by this worker.");
    }

    private void ValidateNextAttempt(DateTime? next, DateTime earliest, bool requireStrictlyAfter)
    {
        if (AttemptsStarted >= MaximumAttempts)
        {
            if (next is not null)
                throw new ArgumentException("Exhausted checks must go to manual review.", nameof(next));
            return;
        }
        if (next is null)
            throw new ArgumentException("A next attempt is required while attempts remain.", nameof(next));
        RequireUtc(next.Value, nameof(next));
        if (next.Value < earliest || (requireStrictlyAfter && next.Value == earliest))
            throw new ArgumentException("The next attempt time is too early.", nameof(next));
    }

    private void ClearLease(DateTime nowUtc)
    {
        LeaseToken = null;
        LeaseExpiresAt = null;
        UpdatedAt = nowUtc;
    }

    private static void RequireUtc(DateTime value, string parameterName)
    {
        if (value.Kind != DateTimeKind.Utc)
            throw new ArgumentException("Time must be UTC.", parameterName);
    }
}
