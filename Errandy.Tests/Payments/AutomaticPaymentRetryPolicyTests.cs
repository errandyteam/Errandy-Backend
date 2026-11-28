using System;
using Errandy.Application.Features.Payments.Reconciliation;
using Xunit;

namespace Errandy.Tests.Payments;

public class AutomaticPaymentRetryPolicyTests
{
    private static readonly DateTime Anchor = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    [Theory]
    [InlineData(0, 5)]
    [InlineData(1, 5)]
    [InlineData(2, 10)]
    [InlineData(3, 20)]
    [InlineData(4, 40)]
    [InlineData(5, 60)]
    [InlineData(9, 60)]
    public void DefaultPolicy_DelaysAndCapsRetries(int attemptsStarted, int expectedMinutes)
    {
        var policy = new AutomaticPaymentRetryPolicy();
        var result = policy.NextAttemptAtUtc(Anchor, attemptsStarted);
        Assert.Equal((DateTime?)Anchor.AddMinutes(expectedMinutes), result);
        Assert.Equal(DateTimeKind.Utc, result!.Value.Kind);
    }

    [Theory]
    [InlineData(10)]
    [InlineData(11)]
    [InlineData(100)]
    public void ExhaustedAttempts_ReturnNoAutomaticRetry(int attemptsStarted)
    {
        var policy = new AutomaticPaymentRetryPolicy();
        Assert.Null(policy.NextAttemptAtUtc(Anchor, attemptsStarted));
    }

    [Fact]
    public void CustomPolicy_UsesConfiguredLimits()
    {
        var policy = new AutomaticPaymentRetryPolicy(3, TimeSpan.FromMinutes(2), TimeSpan.FromMinutes(3), TimeSpan.FromMinutes(4));
        Assert.Equal((DateTime?)Anchor.AddMinutes(2), policy.NextAttemptAtUtc(Anchor, 0));
        Assert.Equal((DateTime?)Anchor.AddMinutes(3), policy.NextAttemptAtUtc(Anchor, 1));
        Assert.Equal((DateTime?)Anchor.AddMinutes(4), policy.NextAttemptAtUtc(Anchor, 2));
        Assert.Null(policy.NextAttemptAtUtc(Anchor, 3));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(101)]
    public void InvalidMaximumAttempts_AreRejected(int maximumAttempts)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new AutomaticPaymentRetryPolicy(maximumAttempts));
    }

    [Fact]
    public void InvalidDurations_AreRejected()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new AutomaticPaymentRetryPolicy(minimumPaymentAge: TimeSpan.Zero));
        Assert.Throws<ArgumentOutOfRangeException>(() => new AutomaticPaymentRetryPolicy(initialRetryDelay: TimeSpan.Zero));
        Assert.Throws<ArgumentOutOfRangeException>(() => new AutomaticPaymentRetryPolicy(initialRetryDelay: TimeSpan.FromHours(2), maximumRetryDelay: TimeSpan.FromHours(1)));
        Assert.Throws<ArgumentOutOfRangeException>(() => new AutomaticPaymentRetryPolicy(minimumPaymentAge: TimeSpan.FromDays(31)));
        Assert.Throws<ArgumentOutOfRangeException>(() => new AutomaticPaymentRetryPolicy(maximumRetryDelay: TimeSpan.FromDays(31)));
    }

    [Fact]
    public void NegativeAttemptCount_IsRejected()
    {
        var policy = new AutomaticPaymentRetryPolicy();
        Assert.Throws<ArgumentOutOfRangeException>(() => policy.NextAttemptAtUtc(Anchor, -1));
    }

    [Theory]
    [InlineData(DateTimeKind.Unspecified)]
    [InlineData(DateTimeKind.Local)]
    public void NonUtcAnchor_IsRejected(DateTimeKind kind)
    {
        var policy = new AutomaticPaymentRetryPolicy();
        var anchor = DateTime.SpecifyKind(Anchor, kind);
        Assert.Throws<ArgumentException>(() => policy.NextAttemptAtUtc(anchor, 0));
    }

    [Fact]
    public void LargeValidAttemptCount_DoesNotOverflowDelay()
    {
        var policy = new AutomaticPaymentRetryPolicy(maximumAttempts: 100);
        Assert.Equal((DateTime?)Anchor.AddHours(1), policy.NextAttemptAtUtc(Anchor, 99));
    }
}
