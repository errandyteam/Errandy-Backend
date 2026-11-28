using System;

namespace Errandy.Application.Features.Escrow;

/// <summary>
/// Pure arithmetic for allocating the current balance of one escrow hold.
/// This does not validate a hold, mutate wallets, record transactions, coordinate
/// concurrent operations, or initiate payments. It is not wired to the service.
/// </summary>
public sealed class EscrowSettlementPlan
{
    private const decimal MaximumMoney = 9999999999999999.99m; // decimal(18,2)

    public decimal HeldAmount { get; }
    public decimal CustomerRefund { get; }
    public decimal RunnerNet { get; }
    public decimal Commission { get; }

    private EscrowSettlementPlan(decimal heldAmount, decimal customerRefund, decimal runnerNet, decimal commission)
    {
        HeldAmount = heldAmount;
        CustomerRefund = customerRefund;
        RunnerNet = runnerNet;
        Commission = commission;
    }

    public static EscrowSettlementPlan FullRefund(decimal heldAmount)
    {
        var held = RoundMoney(heldAmount, nameof(heldAmount), requirePositive: true);
        return new EscrowSettlementPlan(held, held, 0m, 0m);
    }

    public static EscrowSettlementPlan Release(decimal heldAmount, decimal grossRunnerAmount, decimal commissionRate)
    {
        var held = RoundMoney(heldAmount, nameof(heldAmount), requirePositive: true);
        var gross = RoundMoney(grossRunnerAmount, nameof(grossRunnerAmount), requirePositive: true);
        if (gross > held)
            throw new InvalidOperationException("Runner release cannot exceed the held amount.");
        return Allocate(held, gross, commissionRate);
    }

    /// <summary>
    /// Customer cancellation after acceptance: 20% of the estimated cost goes
    /// to the assigned runner, without commission; the remaining hold returns
    /// to the customer. This method does not verify that a runner is assigned.
    /// </summary>
    public static EscrowSettlementPlan CustomerLateCancellation(decimal heldAmount, decimal estimatedCost)
    {
        var held = RoundMoney(heldAmount, nameof(heldAmount), requirePositive: true);
        var estimate = RoundMoney(estimatedCost, nameof(estimatedCost), requirePositive: true);
        // Treat an underfunded hold as a reconciliation issue, not a settlement.
        if (held < estimate)
            throw new InvalidOperationException("Escrow hold is less than the estimated errand cost.");
        var penalty = decimal.Round(estimate * 0.20m, 2, MidpointRounding.ToEven);
        return new EscrowSettlementPlan(held, held - penalty, penalty, 0m);
    }

    public static EscrowSettlementPlan Split(decimal heldAmount, decimal requestedCustomerRefund,
        decimal grossRunnerAmount, decimal commissionRate)
    {
        var held = RoundMoney(heldAmount, nameof(heldAmount), requirePositive: true);
        var requestedRefund = RoundMoney(requestedCustomerRefund, nameof(requestedCustomerRefund), requirePositive: false);
        var gross = RoundMoney(grossRunnerAmount, nameof(grossRunnerAmount), requirePositive: false);
        if (requestedRefund + gross > held)
            throw new InvalidOperationException("Combined settlement cannot exceed the held amount.");
        // Match the existing policy: unallocated held funds go to the customer.
        return Allocate(held, gross, commissionRate);
    }

    private static EscrowSettlementPlan Allocate(decimal held, decimal gross, decimal rate)
    {
        if (rate < 0m || rate >= 1m)
            throw new ArgumentOutOfRangeException(nameof(rate), "Commission rate must be at least zero and less than one.");
        var commission = decimal.Round(gross * rate, 2, MidpointRounding.ToEven);
        return new EscrowSettlementPlan(held, held - gross, gross - commission, commission);
    }

    private static decimal RoundMoney(decimal amount, string name, bool requirePositive)
    {
        if (amount < 0m || amount > MaximumMoney)
            throw new ArgumentOutOfRangeException(name, "Amount is outside the supported money range.");
        var rounded = decimal.Round(amount, 2, MidpointRounding.ToEven);
        if ((requirePositive || amount > 0m) && rounded <= 0m)
            throw new ArgumentOutOfRangeException(name, "Amount must be at least one kobo after rounding.");
        return rounded;
    }
}
