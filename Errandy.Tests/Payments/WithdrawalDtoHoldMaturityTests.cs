using System;
using Errandy.Application.Common.DTOs;
using Errandy.Domain.Entities;
using Errandy.Domain.Enums;
using Xunit;

namespace Errandy.Tests.Payments;

// Pure DTO tests: no database or payout operations.
public class WithdrawalDtoHoldMaturityTests
{
    private static readonly DateTime RequestedAt = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    [Theory]
    [InlineData(0)]
    [InlineData(48)]
    public void PendingPayout_DoesNotReportHoldMatured(int elapsedHours)
    {
        var withdrawal = WithdrawalRequest.CreatePendingPayout(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), 20m,
            "Test Bank", "0000000000", "Test Account", "WDR-payout", RequestedAt);
        var dto = WithdrawalRequestDto.FromEntity(withdrawal, RequestedAt.AddHours(elapsedHours));
        Assert.Equal(WithdrawalStatus.PendingPayout, dto.Status);
        Assert.Equal("PendingPayout", dto.StatusName);
        Assert.False(dto.IsHoldMatured);
    }

    [Theory]
    [InlineData(23, false)]
    [InlineData(24, true)]
    [InlineData(48, true)]
    public void PendingHold_PreservesTimeBasedMaturity(int elapsedHours, bool expected)
    {
        var withdrawal = HeldWithdrawal();
        var dto = WithdrawalRequestDto.FromEntity(withdrawal, RequestedAt.AddHours(elapsedHours));
        Assert.Equal(WithdrawalStatus.PendingHold, dto.Status);
        Assert.Equal(expected, dto.IsHoldMatured);
    }

    [Fact]
    public void CancelledLegacyHold_PreservesPreviousTimeBasedMaturity()
    {
        var withdrawal = HeldWithdrawal();
        withdrawal.CancelByUser(RequestedAt.AddHours(1));
        var dto = WithdrawalRequestDto.FromEntity(withdrawal, RequestedAt.AddHours(48));
        Assert.Equal(WithdrawalStatus.Cancelled, dto.Status);
        Assert.True(dto.IsHoldMatured);
    }

    private static WithdrawalRequest HeldWithdrawal() => WithdrawalRequest.Create(
        Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), 20m,
        "Test Bank", "0000000000", "Test Account", "WDR-hold",
        RequestedAt, TimeSpan.FromHours(24));
}
