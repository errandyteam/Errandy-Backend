using System;
using System.Linq;
using Errandy.Domain.Enums;
using Xunit;

namespace Errandy.Tests.Payments;

// Pure enum compatibility tests: no database or payout operations.
public class WithdrawalStatusCompatibilityTests
{
    [Theory]
    [InlineData(WithdrawalStatus.PendingHold, 1)]
    [InlineData(WithdrawalStatus.Completed, 2)]
    [InlineData(WithdrawalStatus.Cancelled, 3)]
    [InlineData(WithdrawalStatus.Rejected, 4)]
    [InlineData(WithdrawalStatus.PendingPayout, 5)]
    public void StatusValues_AreStableAndDistinct(WithdrawalStatus status, int expectedValue)
    {
        Assert.Equal(expectedValue, (int)status);
    }

    [Fact]
    public void ExistingValues_RemainDistinctFromPendingPayout()
    {
        var values = Enum.GetValues<WithdrawalStatus>().Select(status => (int)status).ToArray();
        Assert.Equal(values.Length, values.Distinct().Count());
        Assert.NotEqual(WithdrawalStatus.PendingHold, WithdrawalStatus.PendingPayout);
        Assert.NotEqual(WithdrawalStatus.Completed, WithdrawalStatus.PendingPayout);
    }
}
