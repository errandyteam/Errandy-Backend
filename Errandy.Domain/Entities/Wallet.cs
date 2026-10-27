using Errandy.Domain.Common;
using TransactionEntity = Errandy.Domain.Entities.Transaction;

namespace Errandy.Domain.Entities;

public class Wallet : BaseEntity
{
    public Guid UserId { get; set; }
    public User? User { get; set; }

    /// <summary>
    /// Spendable / withdrawable balance available to the user.
    /// </summary>
    public decimal Balance { get; set; } = 0m;

    /// <summary>
    /// Funds currently locked in escrow for active errands (prevents double spending).
    /// </summary>
    public decimal LockedBalance { get; set; } = 0m;

    /// <summary>
    /// Total funds owned by the user (Available + Locked).
    /// </summary>
    public decimal TotalBalance => Balance + LockedBalance;

    public string Currency { get; set; } = "NGN";
    public bool IsActive { get; set; } = true;

    public ICollection<TransactionEntity> Transactions { get; set; } = new List<TransactionEntity>();

    public void Credit(decimal amount)
    {
        if (amount <= 0)
            throw new InvalidOperationException("Credit amount must be greater than zero.");

        if (!IsActive)
            throw new InvalidOperationException("Wallet is inactive.");

        Balance += decimal.Round(amount, 2, MidpointRounding.ToEven);
        UpdatedAt = DateTime.UtcNow;
    }

    public void Debit(decimal amount)
    {
        if (amount <= 0)
            throw new InvalidOperationException("Debit amount must be greater than zero.");

        if (!IsActive)
            throw new InvalidOperationException("Wallet is inactive.");

        var rounded = decimal.Round(amount, 2, MidpointRounding.ToEven);
        if (Balance < rounded)
            throw new InvalidOperationException("Insufficient wallet balance.");

        Balance -= rounded;
        UpdatedAt = DateTime.UtcNow;
    }
}