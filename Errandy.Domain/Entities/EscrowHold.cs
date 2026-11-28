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
        if (runnerId == Guid.Empty)
            throw new ArgumentException("RunnerId is required.", nameof(runnerId));
        var net = NonNegativeMoney(netRunnerPayout, nameof(netRunnerPayout));
        var commission = NonNegativeMoney(commissionAmount, nameof(commissionAmount));
        var refund = NonNegativeMoney(unusedBufferRefund, nameof(unusedBufferRefund));
        if (net + commission <= 0m)
            throw new InvalidOperationException("A release must allocate an amount to the runner or commission.");
        EnsureConserved(refund, net, commission);
        var totalRefunded = NonNegativeMoney(RefundedToCustomerAmount + refund, nameof(RefundedToCustomerAmount));

        RunnerId = runnerId;
        ReleasedToRunnerAmount = net;
        CommissionAmount = commission;
        RefundedToCustomerAmount = totalRefunded;
        CurrentLockedAmount = 0m;
        Status = EscrowHoldStatus.Released;
        ReleasedAt = utcNow;
        UpdatedAt = utcNow;
    }

    public void MarkRefunded(decimal refundAmount, DateTime utcNow)
    {
        EnsureHeld(nameof(MarkRefunded));
        var refund = NonNegativeMoney(refundAmount, nameof(refundAmount));
        EnsureConserved(refund, 0m, 0m);
        var totalRefunded = NonNegativeMoney(RefundedToCustomerAmount + refund, nameof(RefundedToCustomerAmount));

        RefundedToCustomerAmount = totalRefunded;
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
        var refund = NonNegativeMoney(customerRefund, nameof(customerRefund));
        var net = NonNegativeMoney(netRunnerPayout, nameof(netRunnerPayout));
        var commission = NonNegativeMoney(commissionAmount, nameof(commissionAmount));
        if (net + commission > 0m && (!runnerId.HasValue || runnerId.Value == Guid.Empty))
            throw new ArgumentException("RunnerId is required for a runner allocation.", nameof(runnerId));
        EnsureConserved(refund, net, commission);
        var totalRefunded = NonNegativeMoney(RefundedToCustomerAmount + refund, nameof(RefundedToCustomerAmount));

        RunnerId = runnerId;
        RefundedToCustomerAmount = totalRefunded;
        ReleasedToRunnerAmount = net;
        CommissionAmount = commission;
        CurrentLockedAmount = 0m;
        Status = EscrowHoldStatus.PartiallyReleased;
        ReleasedAt = net > 0m ? utcNow : null;
        RefundedAt = refund > 0m ? utcNow : null;
        UpdatedAt = utcNow;
    }

    // Domain checks cover only the hold record, not wallet balances, treasury
    // availability, database transactions, or concurrency between operations.
    private void EnsureConserved(decimal refund, decimal net, decimal commission)
    {
        if (refund + net + commission != CurrentLockedAmount)
            throw new InvalidOperationException("Settlement allocation must equal the remaining escrow hold.");
    }

    private static decimal NonNegativeMoney(decimal amount, string parameterName)
    {
        const decimal maximumMoney = 9999999999999999.99m; // decimal(18,2)
        if (amount < 0m || amount > maximumMoney)
            throw new ArgumentOutOfRangeException(parameterName, "Amount is outside the supported money range.");
        var rounded = decimal.Round(amount, 2, MidpointRounding.ToEven);
        if (amount > 0m && rounded == 0m)
            throw new ArgumentOutOfRangeException(parameterName, "Amount must be at least one kobo after rounding.");
        return rounded;
    }

    private void EnsureHeld(string operation)
    {
        if (Status != EscrowHoldStatus.Held)
            throw new InvalidOperationException(
                $"Cannot perform '{operation}' because escrow hold for errand {ErrandId} is already in '{Status}' status.");
    }
}
