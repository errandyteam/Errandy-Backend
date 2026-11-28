namespace Errandy.Domain.Enums;

public enum AutomaticPaymentCheckStatus
{
    Scheduled = 1,
    Leased = 2,
    Confirmed = 3,
    NeedsReview = 4
}

public enum AutomaticPaymentCheckResult
{
    None = 0,
    Confirmed = 1,
    Unconfirmed = 2,
    LeaseExpired = 3
}
