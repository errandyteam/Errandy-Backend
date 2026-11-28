using System;
using Errandy.Domain.Entities;
using Errandy.Domain.Enums;
using Xunit;

namespace Errandy.Tests.Payments;

// Domain-only tests. No handler, database, dispute check, or payout is invoked.
public class WithdrawalPendingPayoutDomainTests
{
    [Fact]
    public void CreatePendingPayout_SetsPendingStatusAndLeavesProcessingFieldsEmpty()
    {
        var userId = Guid.NewGuid();
        var walletId = Guid.NewGuid();
        var transactionId = Guid.NewGuid();
        var requestedAt = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        var withdrawal = WithdrawalRequest.CreatePendingPayout(
            userId, walletId, transactionId, 20m,
            "Test Bank", "0000000000", "Test Account", "WDR-test",
            requestedAt, "000");

        Assert.Equal(WithdrawalStatus.PendingPayout, withdrawal.Status);
        Assert.Equal(userId, withdrawal.UserId);
        Assert.Equal(walletId, withdrawal.WalletId);
        Assert.Equal(transactionId, withdrawal.TransactionId);
        Assert.Equal(20m, withdrawal.Amount);
        Assert.Equal("NGN", withdrawal.Currency);
        Assert.Equal("000", withdrawal.BankCode);
        Assert.Equal(requestedAt, withdrawal.RequestedAt);
        Assert.Equal(requestedAt, withdrawal.EligibleAt);
        Assert.Null(withdrawal.ProcessedAt);
        Assert.Null(withdrawal.ProcessedByAdminId);
        Assert.Null(withdrawal.AdminNote);
    }

    [Fact]
    public void CreatePendingPayout_RejectsInvalidAmount()
    {
        Assert.Throws<ArgumentException>(() => WithdrawalRequest.CreatePendingPayout(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), 0m,
            "Test Bank", "0000000000", "Test Account", "WDR-test",
            new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc)));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(48)]
    public void PendingPayout_HasNoHoldToMature(int elapsedHours)
    {
        var now = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var withdrawal = WithdrawalRequest.CreatePendingPayout(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), 20m,
            "Test Bank", "0000000000", "Test Account", "WDR-test", now);
        Assert.False(withdrawal.IsMatured(now.AddHours(elapsedHours)));
    }

    [Theory]
    [InlineData(23, false)]
    [InlineData(24, true)]
    public void PendingHold_MaturityRemainsTimeBased(int elapsedHours, bool expected)
    {
        var now = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var withdrawal = WithdrawalRequest.Create(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), 20m,
            "Test Bank", "0000000000", "Test Account", "WDR-test", now,
            TimeSpan.FromHours(24));
        Assert.Equal(expected, withdrawal.IsMatured(now.AddHours(elapsedHours)));
    }

    [Fact]
    public void PendingPayout_CannotBeCompletedByExistingMethod()
    {
        var now = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var withdrawal = WithdrawalRequest.CreatePendingPayout(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), 20m,
            "Test Bank", "0000000000", "Test Account", "WDR-test", now);

        Assert.Throws<InvalidOperationException>(() => withdrawal.Complete(now.AddDays(1)));
        Assert.Equal(WithdrawalStatus.PendingPayout, withdrawal.Status);
        Assert.Null(withdrawal.ProcessedAt);
    }
}
