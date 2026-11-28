namespace Errandy.Domain.Enums;

public enum WithdrawalStatus
{
    PendingHold = 1,
    Completed = 2,
    Cancelled = 3,
    Rejected = 4,
    // No security hold, but an actual bank payout is not yet confirmed.
    PendingPayout = 5
}
