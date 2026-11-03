using Errandy.Domain.Entities;
using Errandy.Domain.Enums;

namespace Errandy.Application.Common.DTOs;

public record EscrowHoldDto(
    Guid Id,
    Guid ErrandId,
    Guid CustomerId,
    Guid? RunnerId,
    decimal InitialLockedAmount,
    decimal CurrentLockedAmount,
    decimal ReleasedToRunnerAmount,
    decimal CommissionAmount,
    decimal RefundedToCustomerAmount,
    string Currency,
    EscrowHoldStatus Status,
    string StatusName,
    DateTime CreatedAt,
    DateTime? ReleasedAt,
    DateTime? RefundedAt)
{
    public static EscrowHoldDto FromEntity(EscrowHold hold) => new(
        hold.Id,
        hold.ErrandId,
        hold.CustomerId,
        hold.RunnerId,
        hold.InitialLockedAmount,
        hold.CurrentLockedAmount,
        hold.ReleasedToRunnerAmount,
        hold.CommissionAmount,
        hold.RefundedToCustomerAmount,
        hold.Currency,
        hold.Status,
        hold.Status.ToString(),
        hold.CreatedAt,
        hold.ReleasedAt,
        hold.RefundedAt);
}

public record RunnerEarningsSummaryDto(
    Guid UserId,
    int CompletedPayoutCount,
    int DisputedPayoutCount,
    decimal GrossEarnedAmount,
    decimal TotalCommissionDeducted,
    decimal NetEarnedAmount,
    decimal AvailableWalletBalance,
    string Currency);

public record PlatformFinancialSummaryDto(
    int ActiveEscrowCount,
    decimal TotalActiveEscrowLocked,
    int SettledEscrowCount,
    int DisputedSettlementCount,
    decimal TotalReleasedToRunners,
    decimal TotalCommissionEarned,
    decimal TotalRefundedToCustomers,
    decimal TreasuryWalletBalance,
    string Currency);