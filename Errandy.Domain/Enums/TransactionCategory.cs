namespace Errandy.Domain.Enums;

public enum TransactionCategory
{
    Funding = 1,
    EscrowLock = 2,
    EscrowRelease = 3,
    EscrowRefund = 4,
    Commission = 5,
    Withdrawal = 6,
    Penalty = 7,
    AdminAdjustment = 8
}
