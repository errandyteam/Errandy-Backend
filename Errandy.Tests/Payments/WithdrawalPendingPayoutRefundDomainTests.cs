using System;
using Errandy.Domain.Entities;
using Errandy.Domain.Enums;
using Xunit;

namespace Errandy.Tests.Payments;

// Pure domain tests. They do not refund a wallet, check payout history, invoke
// a handler, connect to a database, or protect against concurrent operations.
public class WithdrawalPendingPayoutRefundDomainTests
{
    private static readonly DateTime RequestedAt = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void PendingPayout_CanTransitionToCancelledOrRejected(bool cancel)
    {
        var withdrawal = PendingPayout();
        var adminId = Guid.NewGuid();
        var processedAt = RequestedAt.AddHours(1);
        if (cancel) withdrawal.CancelByUser(processedAt);
        else withdrawal.Reject(adminId, "  Synthetic reason  ", processedAt);

        Assert.Equal(cancel ? WithdrawalStatus.Cancelled : WithdrawalStatus.Rejected, withdrawal.Status);
        Assert.Equal((DateTime?)processedAt, withdrawal.ProcessedAt);
        Assert.Equal(cancel ? (Guid?)null : adminId, withdrawal.ProcessedByAdminId);
        Assert.Equal(cancel ? "Cancelled by user while pending payout." : "Synthetic reason", withdrawal.AdminNote);
    }

    [Theory]
    [InlineData(true, true)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(false, false)]
    public void TerminalWithdrawal_RejectsAnotherTransition(bool firstCancel, bool secondCancel)
    {
        var withdrawal = PendingPayout();
        var adminId = Guid.NewGuid();
        var processedAt = RequestedAt.AddHours(1);
        if (firstCancel) withdrawal.CancelByUser(processedAt);
        else withdrawal.Reject(adminId, "Synthetic reason", processedAt);
        var expected = firstCancel ? WithdrawalStatus.Cancelled : WithdrawalStatus.Rejected;

        Assert.Throws<InvalidOperationException>(() =>
        {
            if (secondCancel) withdrawal.CancelByUser(processedAt.AddHours(1));
            else withdrawal.Reject(adminId, "Another reason", processedAt.AddHours(1));
        });
        Assert.Equal(expected, withdrawal.Status);
        Assert.Equal((DateTime?)processedAt, withdrawal.ProcessedAt);
    }

    [Fact]
    public void ExistingHeldWithdrawal_CanStillBeCancelled()
    {
        var withdrawal = WithdrawalRequest.Create(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), 20m,
            "Test Bank", "0000000000", "Test Account", "WDR-held",
            RequestedAt, TimeSpan.FromHours(24));
        withdrawal.CancelByUser(RequestedAt.AddHours(1));
        Assert.Equal(WithdrawalStatus.Cancelled, withdrawal.Status);
    }

    private static WithdrawalRequest PendingPayout() => WithdrawalRequest.CreatePendingPayout(
        Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), 20m,
        "Test Bank", "0000000000", "Test Account", "WDR-payout", RequestedAt);
}
