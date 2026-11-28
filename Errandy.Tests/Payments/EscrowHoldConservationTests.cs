using System;
using Errandy.Domain.Entities;
using Errandy.Domain.Enums;
using Xunit;

namespace Errandy.Tests.Payments;

// Pure entity tests. These don't prove wallet balance, treasury funding,
// transaction atomicity, or safety of the disabled escrow service.
public class EscrowHoldConservationTests
{
    private static readonly DateTime Now = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void Refund_RequiresAndRecordsTheEntireRemainingHold()
    {
        var hold = Held();
        hold.MarkRefunded(20m, Now);
        Assert.Equal(EscrowHoldStatus.Refunded, hold.Status);
        Assert.Equal(0m, hold.CurrentLockedAmount);
        Assert.Equal(20m, hold.RefundedToCustomerAmount);
        Assert.Equal((DateTime?)Now, hold.RefundedAt);
    }

    [Fact]
    public void Refund_MismatchedAmountsLeaveHoldUnchanged()
    {
        var hold = Held();
        Assert.Throws<InvalidOperationException>(() => hold.MarkRefunded(19m, Now));
        Assert.Throws<InvalidOperationException>(() => hold.MarkRefunded(21m, Now));
        AssertUnchanged(hold, 20m);
    }

    [Fact]
    public void Release_AccountsForRunnerCommissionAndUnusedBuffer()
    {
        var hold = Held();
        var runner = Guid.NewGuid();
        hold.MarkReleased(runner, 13.50m, 1.50m, 5m, Now);
        Assert.Equal(EscrowHoldStatus.Released, hold.Status);
        Assert.Equal((Guid?)runner, hold.RunnerId);
        Assert.Equal(13.50m, hold.ReleasedToRunnerAmount);
        Assert.Equal(1.50m, hold.CommissionAmount);
        Assert.Equal(5m, hold.RefundedToCustomerAmount);
        Assert.Equal(0m, hold.CurrentLockedAmount);
    }

    [Fact]
    public void Release_RejectsUnderAndOverAllocationWithoutMutation()
    {
        var hold = Held();
        Assert.Throws<InvalidOperationException>(() => hold.MarkReleased(Guid.NewGuid(), 10m, 1m, 5m, Now));
        Assert.Throws<InvalidOperationException>(() => hold.MarkReleased(Guid.NewGuid(), 15m, 1m, 5m, Now));
        AssertUnchanged(hold, 20m);
    }

    [Fact]
    public void Split_RequiresConservationAndRecordsAllParts()
    {
        var hold = Held();
        hold.MarkPartiallyReleased(Guid.NewGuid(), 10m, 9m, 1m, Now);
        Assert.Equal(EscrowHoldStatus.PartiallyReleased, hold.Status);
        Assert.Equal(10m, hold.RefundedToCustomerAmount);
        Assert.Equal(9m, hold.ReleasedToRunnerAmount);
        Assert.Equal(1m, hold.CommissionAmount);
        Assert.Equal(0m, hold.CurrentLockedAmount);
    }

    [Fact]
    public void Split_RejectsUnderAndOverAllocationWithoutMutation()
    {
        var hold = Held();
        Assert.Throws<InvalidOperationException>(() => hold.MarkPartiallyReleased(Guid.NewGuid(), 5m, 5m, 1m, Now));
        Assert.Throws<InvalidOperationException>(() => hold.MarkPartiallyReleased(Guid.NewGuid(), 15m, 5m, 1m, Now));
        AssertUnchanged(hold, 20m);
    }

    [Fact]
    public void NegativeOrSubKoboAmounts_AreRejected()
    {
        var hold = Held();
        Assert.Throws<ArgumentOutOfRangeException>(() => hold.MarkRefunded(-1m, Now));
        Assert.Throws<ArgumentOutOfRangeException>(() => hold.MarkRefunded(0.004m, Now));
        Assert.Throws<ArgumentOutOfRangeException>(() => hold.MarkReleased(Guid.NewGuid(), -1m, 1m, 20m, Now));
        AssertUnchanged(hold, 20m);
    }

    [Fact]
    public void RunnerAllocation_RequiresRunnerId()
    {
        var hold = Held();
        Assert.Throws<ArgumentException>(() => hold.MarkReleased(Guid.Empty, 19m, 1m, 0m, Now));
        Assert.Throws<ArgumentException>(() => hold.MarkPartiallyReleased(null, 10m, 9m, 1m, Now));
        AssertUnchanged(hold, 20m);
    }

    [Fact]
    public void PreviousDecrease_IsNotCountedTwiceDuringFinalSettlement()
    {
        var hold = Held();
        hold.DecreaseHold(5m);
        hold.MarkPartiallyReleased(Guid.NewGuid(), 3m, 10m, 2m, Now);
        Assert.Equal(8m, hold.RefundedToCustomerAmount);
        Assert.Equal(10m, hold.ReleasedToRunnerAmount);
        Assert.Equal(2m, hold.CommissionAmount);
        Assert.Equal(0m, hold.CurrentLockedAmount);
    }

    [Fact]
    public void CompletedHold_RejectsAnotherSettlement()
    {
        var hold = Held();
        hold.MarkRefunded(20m, Now);
        Assert.Throws<InvalidOperationException>(() => hold.MarkRefunded(20m, Now));
        Assert.Equal(EscrowHoldStatus.Refunded, hold.Status);
        Assert.Equal(20m, hold.RefundedToCustomerAmount);
    }

    private static EscrowHold Held() => EscrowHold.Create(Guid.NewGuid(), Guid.NewGuid(), 20m);

    private static void AssertUnchanged(EscrowHold hold, decimal expectedLocked)
    {
        Assert.Equal(EscrowHoldStatus.Held, hold.Status);
        Assert.Equal(expectedLocked, hold.CurrentLockedAmount);
        Assert.Equal(0m, hold.RefundedToCustomerAmount);
        Assert.Equal(0m, hold.ReleasedToRunnerAmount);
        Assert.Equal(0m, hold.CommissionAmount);
        Assert.Null(hold.RunnerId);
        Assert.Null(hold.RefundedAt);
        Assert.Null(hold.ReleasedAt);
    }
}
