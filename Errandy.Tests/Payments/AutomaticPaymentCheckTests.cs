using System;
using Errandy.Domain.Entities;
using Errandy.Domain.Enums;
using Xunit;

namespace Errandy.Tests.Payments;

// Pure state-model tests. These do not test database concurrency or a worker.
public class AutomaticPaymentCheckTests
{
    private static readonly DateTime Due = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
    private static readonly TimeSpan LeaseDuration = TimeSpan.FromMinutes(2);

    [Fact]
    public void Schedule_CreatesUnclaimedRecord()
    {
        var intentId = Guid.NewGuid();
        var check = AutomaticPaymentCheck.Schedule(intentId, Due, 10);
        Assert.Equal(intentId, check.PaymentIntentId);
        Assert.Equal(AutomaticPaymentCheckStatus.Scheduled, check.Status);
        Assert.Equal(AutomaticPaymentCheckResult.None, check.LastResult);
        Assert.Equal(0, check.AttemptsStarted);
        Assert.Equal(10, check.MaximumAttempts);
        Assert.Equal((DateTime?)Due, check.NextAttemptAt);
        Assert.Null(check.LeaseToken);
        Assert.Null(check.LeaseExpiresAt);
        Assert.Equal(0L, check.Revision);
    }

    [Fact]
    public void Schedule_RejectsInvalidInput()
    {
        Assert.Throws<ArgumentException>(() => AutomaticPaymentCheck.Schedule(Guid.Empty, Due, 10));
        Assert.Throws<ArgumentException>(() => AutomaticPaymentCheck.Schedule(Guid.NewGuid(), DateTime.SpecifyKind(Due, DateTimeKind.Unspecified), 10));
        Assert.Throws<ArgumentOutOfRangeException>(() => AutomaticPaymentCheck.Schedule(Guid.NewGuid(), Due, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => AutomaticPaymentCheck.Schedule(Guid.NewGuid(), Due, 101));
    }

    [Fact]
    public void TryStart_RespectsDueTime_AndCountsStartedAttempt()
    {
        var check = New();
        var token = Guid.NewGuid();
        Assert.False(check.TryStart(token, Due.AddTicks(-1), LeaseDuration));
        Assert.Equal(0, check.AttemptsStarted);
        Assert.Equal(0L, check.Revision);
        Assert.True(check.TryStart(token, Due, LeaseDuration));
        Assert.Equal(1, check.AttemptsStarted);
        Assert.Equal(AutomaticPaymentCheckStatus.Leased, check.Status);
        Assert.Equal((Guid?)token, check.LeaseToken);
        Assert.Equal((DateTime?)Due.Add(LeaseDuration), check.LeaseExpiresAt);
        Assert.Equal((DateTime?)Due, check.LastStartedAt);
        Assert.Null(check.NextAttemptAt);
        Assert.Equal(1L, check.Revision);
        Assert.False(check.TryStart(Guid.NewGuid(), Due, LeaseDuration));
    }

    [Fact]
    public void TryStart_RejectsInvalidInputsWithoutMutation()
    {
        var check = New();
        Assert.Throws<ArgumentException>(() => check.TryStart(Guid.Empty, Due, LeaseDuration));
        Assert.Throws<ArgumentException>(() => check.TryStart(Guid.NewGuid(), DateTime.SpecifyKind(Due, DateTimeKind.Local), LeaseDuration));
        Assert.Throws<ArgumentOutOfRangeException>(() => check.TryStart(Guid.NewGuid(), Due, TimeSpan.Zero));
        Assert.Throws<ArgumentOutOfRangeException>(() => check.TryStart(Guid.NewGuid(), Due, TimeSpan.FromHours(25)));
        Assert.Equal(AutomaticPaymentCheckStatus.Scheduled, check.Status);
        Assert.Equal(0, check.AttemptsStarted);
        Assert.Equal(0L, check.Revision);
    }

    [Fact]
    public void Confirm_WithLiveOwner_ClearsLease()
    {
        var check = New();
        var token = Guid.NewGuid();
        check.TryStart(token, Due, LeaseDuration);
        check.MarkConfirmed(token, Due.AddMinutes(1));
        Assert.Equal(AutomaticPaymentCheckStatus.Confirmed, check.Status);
        Assert.Equal(AutomaticPaymentCheckResult.Confirmed, check.LastResult);
        Assert.Equal((DateTime?)Due.AddMinutes(1), check.LastFinishedAt);
        Assert.Null(check.LeaseToken);
        Assert.Null(check.LeaseExpiresAt);
        Assert.Null(check.NextAttemptAt);
        Assert.Equal(2L, check.Revision);
        Assert.False(check.TryStart(Guid.NewGuid(), Due.AddHours(1), LeaseDuration));
        Assert.Throws<InvalidOperationException>(() => check.MarkConfirmed(token, Due.AddMinutes(1)));
    }

    [Fact]
    public void WrongTokenOrExpiredLease_CannotFinalize()
    {
        var check = New();
        var token = Guid.NewGuid();
        check.TryStart(token, Due, LeaseDuration);
        Assert.Throws<InvalidOperationException>(() => check.MarkConfirmed(Guid.NewGuid(), Due.AddMinutes(1)));
        Assert.Throws<InvalidOperationException>(() => check.MarkConfirmed(token, Due.AddTicks(-1)));
        Assert.Throws<InvalidOperationException>(() => check.MarkConfirmed(token, Due.Add(LeaseDuration)));
        Assert.Throws<InvalidOperationException>(() => check.MarkUnconfirmed(token, Due.Add(LeaseDuration), Due.AddMinutes(8)));
        Assert.Equal(AutomaticPaymentCheckStatus.Leased, check.Status);
        Assert.Equal((Guid?)token, check.LeaseToken);
        Assert.Equal(1L, check.Revision);
    }

    [Fact]
    public void Unconfirmed_SchedulesFutureRetry_AndCountsNextStart()
    {
        var check = New();
        var token = Guid.NewGuid();
        check.TryStart(token, Due, LeaseDuration);
        var finished = Due.AddMinutes(1);
        var next = finished.AddMinutes(5);
        check.MarkUnconfirmed(token, finished, next);
        Assert.Equal(AutomaticPaymentCheckStatus.Scheduled, check.Status);
        Assert.Equal(AutomaticPaymentCheckResult.Unconfirmed, check.LastResult);
        Assert.Equal((DateTime?)next, check.NextAttemptAt);
        Assert.Null(check.LeaseToken);
        Assert.Equal(1, check.AttemptsStarted);
        Assert.Equal(2L, check.Revision);
        Assert.False(check.TryStart(Guid.NewGuid(), next.AddTicks(-1), LeaseDuration));
        Assert.True(check.TryStart(Guid.NewGuid(), next, LeaseDuration));
        Assert.Equal(2, check.AttemptsStarted);
        Assert.Equal(3L, check.Revision);
    }

    [Fact]
    public void Unconfirmed_RejectsInvalidRetryWithoutChangingLease()
    {
        var check = New();
        var token = Guid.NewGuid();
        check.TryStart(token, Due, LeaseDuration);
        var now = Due.AddMinutes(1);
        Assert.Throws<ArgumentException>(() => check.MarkUnconfirmed(token, now, null));
        Assert.Throws<ArgumentException>(() => check.MarkUnconfirmed(token, now, now));
        Assert.Throws<ArgumentException>(() => check.MarkUnconfirmed(token, now, DateTime.SpecifyKind(now.AddMinutes(1), DateTimeKind.Unspecified)));
        Assert.Equal(AutomaticPaymentCheckStatus.Leased, check.Status);
        Assert.Equal((Guid?)token, check.LeaseToken);
        Assert.Equal(1L, check.Revision);
    }

    [Fact]
    public void LastUnconfirmedAttempt_GoesToReview_NotFailed()
    {
        var check = AutomaticPaymentCheck.Schedule(Guid.NewGuid(), Due, 1);
        var token = Guid.NewGuid();
        check.TryStart(token, Due, LeaseDuration);
        Assert.Throws<ArgumentException>(() => check.MarkUnconfirmed(token, Due.AddMinutes(1), Due.AddMinutes(6)));
        check.MarkUnconfirmed(token, Due.AddMinutes(1), null);
        Assert.Equal(AutomaticPaymentCheckStatus.NeedsReview, check.Status);
        Assert.Equal(AutomaticPaymentCheckResult.Unconfirmed, check.LastResult);
        Assert.Equal(1, check.AttemptsStarted);
        Assert.Null(check.NextAttemptAt);
        Assert.Null(check.LeaseToken);
        Assert.False(check.TryStart(Guid.NewGuid(), Due.AddHours(1), LeaseDuration));
    }

    [Fact]
    public void ExpiredLease_RecoveryCountsAttempt_AndRejectsStaleOwner()
    {
        var check = New();
        var oldToken = Guid.NewGuid();
        check.TryStart(oldToken, Due, LeaseDuration);
        var expiry = Due.Add(LeaseDuration);
        var next = expiry.AddMinutes(5);
        Assert.Throws<InvalidOperationException>(() => check.RecoverExpiredLease(expiry.AddTicks(-1), next));
        Assert.Throws<ArgumentException>(() => check.RecoverExpiredLease(expiry, expiry.AddTicks(-1)));
        // A late recovery may use a retry time that is already due.
        check.RecoverExpiredLease(next.AddMinutes(1), next);
        Assert.Equal(AutomaticPaymentCheckStatus.Scheduled, check.Status);
        Assert.Equal(AutomaticPaymentCheckResult.LeaseExpired, check.LastResult);
        Assert.Equal(1, check.AttemptsStarted);
        Assert.Equal(2L, check.Revision);
        var newToken = Guid.NewGuid();
        Assert.True(check.TryStart(newToken, next.AddMinutes(1), LeaseDuration));
        Assert.Throws<InvalidOperationException>(() => check.MarkConfirmed(oldToken, next.AddMinutes(1)));
        Assert.Equal((Guid?)newToken, check.LeaseToken);
    }

    [Fact]
    public void LastExpiredLease_GoesToReview()
    {
        var check = AutomaticPaymentCheck.Schedule(Guid.NewGuid(), Due, 1);
        check.TryStart(Guid.NewGuid(), Due, LeaseDuration);
        var expiry = Due.Add(LeaseDuration);
        Assert.Throws<ArgumentException>(() => check.RecoverExpiredLease(expiry, expiry.AddMinutes(5)));
        check.RecoverExpiredLease(expiry, null);
        Assert.Equal(AutomaticPaymentCheckStatus.NeedsReview, check.Status);
        Assert.Equal(AutomaticPaymentCheckResult.LeaseExpired, check.LastResult);
        Assert.Equal(1, check.AttemptsStarted);
        Assert.Null(check.LeaseToken);
        Assert.Null(check.NextAttemptAt);
    }

    [Fact]
    public void RecoveryWithoutLease_IsRejected()
    {
        var check = New();
        Assert.Throws<InvalidOperationException>(() => check.RecoverExpiredLease(Due.AddHours(1), Due.AddHours(2)));
        Assert.Equal(0L, check.Revision);
    }

    private static AutomaticPaymentCheck New() => AutomaticPaymentCheck.Schedule(Guid.NewGuid(), Due, 10);
}
