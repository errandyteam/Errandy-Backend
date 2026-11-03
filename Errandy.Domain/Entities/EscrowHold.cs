using Errandy.Domain.Common;
using Errandy.Domain.Enums;

namespace Errandy.Domain.Entities;

/// <summary>
/// Tracks the exact escrow lifecycle for a single errand so multiple concurrent
/// errands per customer never mix locked balances, and unused buffers are accurately refunded.
/// </summary>
public class EscrowHold : BaseEntity
{
    public Guid ErrandId { get; private set; }
    public Guid CustomerId { get; private set; }
    public Guid? RunnerId { get; private set; }

    public decimal InitialLockedAmount { get; private set; }
    public decimal CurrentLockedAmount { get; private set; }
    public decimal ReleasedToRunnerAmount { get; private set; }
    public decimal CommissionAmount { get; private set; }
    public decimal RefundedToCustomerAmount { get; private set; }

    public string Currency { get; private set; } = "NGN";
    public EscrowHoldStatus Status { get; private set; } = EscrowHoldStatus.Held;

    public DateTime? ReleasedAt { get; private set; }
    public DateTime? RefundedAt { get; private set; }

    private EscrowHold() { }

    public static EscrowHold Create(Guid errandId, Guid customerId, decimal amount)
    {
        if (errandId == Guid.Empty)
            throw new ArgumentException("ErrandId is required.", nameof(errandId));

        if (customerId == Guid.Empty)
            throw new ArgumentException("CustomerId is required.", nameof(customerId));

        if (amount <= 0)
            throw new ArgumentException("Escrow lock amount must be greater than zero.", nameof(amount));

        var rounded = decimal.Round(amount, 2, MidpointRounding.ToEven);

        return new EscrowHold
        {
            ErrandId = errandId,
            CustomerId = customerId,
            InitialLockedAmount = rounded,
            CurrentLockedAmount = rounded,
            Status = EscrowHoldStatus.Held
        };
    }

    public void IncreaseHold(decimal additionalAmount)
    {
        EnsureHeld(nameof(IncreaseHold));
        if (additionalAmount <= 0)
            throw new InvalidOperationException("Additional lock amount must be greater than zero.");

        var rounded = decimal.Round(additionalAmount, 2, MidpointRounding.ToEven);
        CurrentLockedAmount += rounded;
        UpdatedAt = DateTime.UtcNow;
    }

    public void DecreaseHold(decimal unlockedAmount)
    {
        EnsureHeld(nameof(DecreaseHold));
        if (unlockedAmount <= 0)
            throw new InvalidOperationException("Unlocked amount must be greater than zero.");

        var rounded = decimal.Round(unlockedAmount, 2, MidpointRounding.ToEven);
        if (rounded > CurrentLockedAmount)
            throw new InvalidOperationException("Cannot decrease hold by more than the current locked amount.");

        CurrentLockedAmount -= rounded;
        RefundedToCustomerAmount += rounded;
        UpdatedAt = DateTime.UtcNow;
    }

    public void MarkReleased(
        Guid runnerId,
        decimal netRunnerPayout,
        decimal commissionAmount,
        decimal unusedBufferRefund,
        DateTime utcNow)
    {
        EnsureHeld(nameof(MarkReleased));

        RunnerId = runnerId;
        ReleasedToRunnerAmount = decimal.Round(netRunnerPayout, 2, MidpointRounding.ToEven);
        CommissionAmount = decimal.Round(commissionAmount, 2, MidpointRounding.ToEven);
        RefundedToCustomerAmount += decimal.Round(unusedBufferRefund, 2, MidpointRounding.ToEven);
        CurrentLockedAmount = 0m;
        Status = EscrowHoldStatus.Released;
        ReleasedAt = utcNow;
        UpdatedAt = utcNow;
    }

    public void MarkRefunded(decimal refundAmount, DateTime utcNow)
    {
        EnsureHeld(nameof(MarkRefunded));

        RefundedToCustomerAmount += decimal.Round(refundAmount, 2, MidpointRounding.ToEven);
        CurrentLockedAmount = 0m;
        Status = EscrowHoldStatus.Refunded;
        RefundedAt = utcNow;
        UpdatedAt = utcNow;
    }

    public void MarkPartiallyReleased(
        Guid? runnerId,
        decimal customerRefund,
        decimal netRunnerPayout,
        decimal commissionAmount,
        DateTime utcNow)
    {
        EnsureHeld(nameof(MarkPartiallyReleased));

        RunnerId = runnerId;
        RefundedToCustomerAmount += decimal.Round(customerRefund, 2, MidpointRounding.ToEven);
        ReleasedToRunnerAmount = decimal.Round(netRunnerPayout, 2, MidpointRounding.ToEven);
        CommissionAmount = decimal.Round(commissionAmount, 2, MidpointRounding.ToEven);
        CurrentLockedAmount = 0m;
        Status = EscrowHoldStatus.PartiallyReleased;
        ReleasedAt = netRunnerPayout > 0 ? utcNow : null;
        RefundedAt = customerRefund > 0 ? utcNow : null;
        UpdatedAt = utcNow;
    }

    private void EnsureHeld(string operation)
    {
        if (Status != EscrowHoldStatus.Held)
            throw new InvalidOperationException(
                $"Cannot perform '{operation}' because escrow hold for errand {ErrandId} is already in '{Status}' status.");
    }
}