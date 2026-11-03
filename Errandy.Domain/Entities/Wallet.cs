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

    /// <summary>
    /// Moves funds from spendable Balance into LockedBalance (Escrow hold).
    /// Prevents double-spending while an errand is active.
    /// </summary>
    public void LockFunds(decimal amount)
    {
        if (amount <= 0)
            throw new InvalidOperationException("Lock amount must be greater than zero.");

        if (!IsActive)
            throw new InvalidOperationException("Wallet is inactive.");

        var rounded = decimal.Round(amount, 2, MidpointRounding.ToEven);
        if (Balance < rounded)
            throw new InvalidOperationException("Insufficient available wallet balance to lock in escrow.");

        Balance -= rounded;
        LockedBalance += rounded;
        UpdatedAt = DateTime.UtcNow;
    }

    /// <summary>
    /// Deducts funds from LockedBalance when releasing escrow to the runner.
    /// </summary>
    public void ConsumeLockedFunds(decimal amount)
    {
        if (amount <= 0)
            throw new InvalidOperationException("Release amount must be greater than zero.");

        var rounded = decimal.Round(amount, 2, MidpointRounding.ToEven);
        if (LockedBalance < rounded)
            throw new InvalidOperationException("Insufficient locked escrow balance.");

        LockedBalance -= rounded;
        UpdatedAt = DateTime.UtcNow;
    }

    /// <summary>
    /// Returns locked escrow funds back to the customer's available Balance (unused buffer or cancellation refund).
    /// </summary>
    public void RefundLockedFunds(decimal amount)
    {
        if (amount <= 0)
            throw new InvalidOperationException("Refund amount must be greater than zero.");

        var rounded = decimal.Round(amount, 2, MidpointRounding.ToEven);
        if (LockedBalance < rounded)
            throw new InvalidOperationException("Refund amount exceeds locked escrow balance.");

        LockedBalance -= rounded;
        Balance += rounded;
        UpdatedAt = DateTime.UtcNow;
    }

    /// <summary>
    /// Restores debited funds from a cancelled or admin-rejected withdrawal (works even if wallet is frozen).
    /// </summary>
    public void RestoreWithheldFunds(decimal amount)
    {
        if (amount <= 0)
            throw new InvalidOperationException("Restore amount must be greater than zero.");

        Balance += decimal.Round(amount, 2, MidpointRounding.ToEven);
        UpdatedAt = DateTime.UtcNow;
    }

    /// <summary>
    /// Admin credit adjustment (bypasses IsActive check so admins can reconcile frozen wallets).
    /// </summary>
    public void AdminCredit(decimal amount)
    {
        if (amount <= 0)
            throw new InvalidOperationException("Adjustment credit amount must be greater than zero.");

        Balance += decimal.Round(amount, 2, MidpointRounding.ToEven);
        UpdatedAt = DateTime.UtcNow;
    }

    /// <summary>
    /// Admin debit / penalty adjustment (bypasses IsActive check while still preventing negative balances).
    /// </summary>
    public void AdminDebit(decimal amount)
    {
        if (amount <= 0)
            throw new InvalidOperationException("Adjustment debit amount must be greater than zero.");

        var rounded = decimal.Round(amount, 2, MidpointRounding.ToEven);
        if (Balance < rounded)
            throw new InvalidOperationException("Insufficient available wallet balance for admin debit.");

        Balance -= rounded;
        UpdatedAt = DateTime.UtcNow;
    }

    public void SetActiveStatus(bool isActive)
    {
        IsActive = isActive;
        UpdatedAt = DateTime.UtcNow;
    }
}
