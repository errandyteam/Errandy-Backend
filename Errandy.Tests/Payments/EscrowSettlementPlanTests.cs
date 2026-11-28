using System;
using Errandy.Application.Features.Escrow;
using Xunit;

namespace Errandy.Tests.Payments;

// Pure arithmetic only: these tests do not prove a hold exists, funds are
// available, a treasury is present, or the disabled escrow service is safe.
public class EscrowSettlementPlanTests
{
    [Fact]
    public void SettlementPlan_ComesFromApplicationAssembly()
    {
        Assert.Equal("Errandy.Application", typeof(EscrowSettlementPlan).Assembly.GetName().Name);
    }

    [Fact]
    public void FullRefund_AllocatesEntireHoldToCustomer()
    {
        var plan = EscrowSettlementPlan.FullRefund(20m);
        Assert.Equal(20m, plan.CustomerRefund);
        Assert.Equal(0m, plan.RunnerNet);
        Assert.Equal(0m, plan.Commission);
        AssertConserved(plan);
    }

    [Fact]
    public void Release_ReturnsUnusedBufferAndAccountsForCommission()
    {
        var plan = EscrowSettlementPlan.Release(20m, 15m, 0.10m);
        Assert.Equal(5m, plan.CustomerRefund);
        Assert.Equal(13.50m, plan.RunnerNet);
        Assert.Equal(1.50m, plan.Commission);
        AssertConserved(plan);
    }

    [Fact]
    public void Split_AllocatesUnassignedRemainderToCustomer()
    {
        var plan = EscrowSettlementPlan.Split(20m, 6m, 10m, 0.10m);
        Assert.Equal(10m, plan.CustomerRefund);
        Assert.Equal(9m, plan.RunnerNet);
        Assert.Equal(1m, plan.Commission);
        AssertConserved(plan);
    }

    [Fact]
    public void Split_WithNoRunnerReleaseRefundsWholeHold()
    {
        var plan = EscrowSettlementPlan.Split(20m, 5m, 0m, 0.10m);
        Assert.Equal(20m, plan.CustomerRefund);
        Assert.Equal(0m, plan.RunnerNet);
        Assert.Equal(0m, plan.Commission);
        AssertConserved(plan);
    }

    [Fact]
    public void Commission_UsesBankersRoundingAndStillConserves()
    {
        var plan = EscrowSettlementPlan.Release(0.05m, 0.05m, 0.10m);
        Assert.Equal(0m, plan.Commission);
        Assert.Equal(0.05m, plan.RunnerNet);
        AssertConserved(plan);
    }

    [Fact]
    public void Release_RejectsMoreThanHeld()
    {
        Assert.Throws<InvalidOperationException>(() => EscrowSettlementPlan.Release(20m, 21m, 0.10m));
    }

    [Fact]
    public void Split_RejectsAllocationsExceedingHold()
    {
        Assert.Throws<InvalidOperationException>(() => EscrowSettlementPlan.Split(20m, 11m, 10m, 0.10m));
    }

    [Fact]
    public void Split_RejectsNegativeAmounts()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => EscrowSettlementPlan.Split(20m, -1m, 5m, 0.10m));
        Assert.Throws<ArgumentOutOfRangeException>(() => EscrowSettlementPlan.Split(20m, 1m, -5m, 0.10m));
    }

    [Fact]
    public void InvalidCommissionRates_AreRejected()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => EscrowSettlementPlan.Release(20m, 10m, -0.01m));
        Assert.Throws<ArgumentOutOfRangeException>(() => EscrowSettlementPlan.Release(20m, 10m, 1m));
        Assert.Throws<ArgumentOutOfRangeException>(() => EscrowSettlementPlan.Split(20m, 5m, 0m, 1m));
    }

    [Fact]
    public void AmountsRoundingBelowOneKobo_AreRejectedRatherThanSilentlyIgnored()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => EscrowSettlementPlan.FullRefund(0.004m));
        Assert.Throws<ArgumentOutOfRangeException>(() => EscrowSettlementPlan.Split(20m, 0.004m, 10m, 0.10m));
        Assert.Throws<ArgumentOutOfRangeException>(() => EscrowSettlementPlan.Release(20m, 0.004m, 0.10m));
    }

    [Fact]
    public void AmountBeyondDecimal18_2_IsRejected()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => EscrowSettlementPlan.FullRefund(10000000000000000m));
    }

    [Fact]
    public void LateCancellation_ReturnsUnusedBufferAndPaysPenaltyToRunner()
    {
        var plan = EscrowSettlementPlan.CustomerLateCancellation(120m, 100m);
        Assert.Equal(100m, plan.CustomerRefund);
        Assert.Equal(20m, plan.RunnerNet);
        Assert.Equal(0m, plan.Commission);
        AssertConserved(plan);
    }

    [Fact]
    public void LateCancellation_WithoutBufferSplitsTheHold()
    {
        var plan = EscrowSettlementPlan.CustomerLateCancellation(100m, 100m);
        Assert.Equal(80m, plan.CustomerRefund);
        Assert.Equal(20m, plan.RunnerNet);
        Assert.Equal(0m, plan.Commission);
        AssertConserved(plan);
    }

    [Fact]
    public void LateCancellation_RoundsPenaltyToKobo()
    {
        var plan = EscrowSettlementPlan.CustomerLateCancellation(0.05m, 0.05m);
        Assert.Equal(0.04m, plan.CustomerRefund);
        Assert.Equal(0.01m, plan.RunnerNet);
        AssertConserved(plan);
    }

    [Fact]
    public void LateCancellation_PenaltyMayRoundToZero()
    {
        var plan = EscrowSettlementPlan.CustomerLateCancellation(0.01m, 0.01m);
        Assert.Equal(0.01m, plan.CustomerRefund);
        Assert.Equal(0m, plan.RunnerNet);
        Assert.Equal(0m, plan.Commission);
        AssertConserved(plan);
    }

    [Fact]
    public void LateCancellation_RejectsUnderfundedHold()
    {
        Assert.Throws<InvalidOperationException>(() =>
            EscrowSettlementPlan.CustomerLateCancellation(99.99m, 100m));
    }

    [Fact]
    public void LateCancellation_RejectsInvalidMoneyInputs()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => EscrowSettlementPlan.CustomerLateCancellation(0m, 1m));
        Assert.Throws<ArgumentOutOfRangeException>(() => EscrowSettlementPlan.CustomerLateCancellation(1m, 0m));
        Assert.Throws<ArgumentOutOfRangeException>(() => EscrowSettlementPlan.CustomerLateCancellation(1m, -1m));
    }

    private static void AssertConserved(EscrowSettlementPlan plan) =>
        Assert.Equal(plan.HeldAmount, plan.CustomerRefund + plan.RunnerNet + plan.Commission);
}
