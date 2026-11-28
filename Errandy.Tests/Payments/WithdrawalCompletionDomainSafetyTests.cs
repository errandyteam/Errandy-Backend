using System;
using Errandy.Domain.Entities;
using Errandy.Domain.Enums;
using Xunit;

namespace Errandy.Tests.Payments;

// Pure domain tests: no database, API, gateway or bank transfer.
public class WithdrawalCompletionDomainSafetyTests
{
    [Theory]
    [InlineData(false, 0)]
    [InlineData(false, 48)]
    [InlineData(true, 0)]
    [InlineData(true, 48)]
    public void Complete_IsBlockedAndDoesNotChangeWithdrawal(bool withAdmin, int elapsedHours)
    {
        var requested = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var withdrawal = WithdrawalRequest.Create(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), 20m,
            "Test Bank", "0000000000", "Test Account", "WDR-test",
            requested, TimeSpan.FromHours(24));
        Guid? adminId = withAdmin ? Guid.NewGuid() : null;

        var error = Assert.Throws<InvalidOperationException>(() =>
            withdrawal.Complete(requested.AddHours(elapsedHours), adminId, "Test note"));

        Assert.Contains("disabled", error.Message);
        Assert.Equal(WithdrawalStatus.PendingHold, withdrawal.Status);
        Assert.Equal(requested.AddHours(24), withdrawal.EligibleAt);
        Assert.Null(withdrawal.ProcessedAt);
        Assert.Null(withdrawal.ProcessedByAdminId);
        Assert.Null(withdrawal.AdminNote);
    }
}
