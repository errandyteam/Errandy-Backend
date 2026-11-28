using System;
using Errandy.Application.Features.Escrow;
using Errandy.Domain.Entities;
using Xunit;

namespace Errandy.Tests.Payments;

// Pure validation tests: no database, escrow service, payout or wallet mutation.
public class EscrowSettlementPreconditionsTests
{
    [Fact]
    public void MatchingHeldEscrowWithBackedWallet_IsAccepted()
    {
        var f = Fixture();
        EscrowSettlementPreconditions.EnsureCustomerBacking(f.Hold, f.Wallet, f.ErrandId, f.CustomerId);
    }

    [Fact]
    public void MissingHold_IsRejected()
    {
        var f = Fixture();
        Assert.Throws<InvalidOperationException>(() =>
            EscrowSettlementPreconditions.EnsureCustomerBacking(null, f.Wallet, f.ErrandId, f.CustomerId));
    }

    [Fact]
    public void MissingWallet_IsRejected()
    {
        var f = Fixture();
        Assert.Throws<InvalidOperationException>(() =>
            EscrowSettlementPreconditions.EnsureCustomerBacking(f.Hold, null, f.ErrandId, f.CustomerId));
    }

    [Fact]
    public void WrongErrandOrCustomer_IsRejected()
    {
        var f = Fixture();
        Assert.Throws<InvalidOperationException>(() =>
            EscrowSettlementPreconditions.EnsureCustomerBacking(f.Hold, f.Wallet, Guid.NewGuid(), f.CustomerId));
        Assert.Throws<InvalidOperationException>(() =>
            EscrowSettlementPreconditions.EnsureCustomerBacking(f.Hold, f.Wallet, f.ErrandId, Guid.NewGuid()));
    }

    [Fact]
    public void WrongOrEmptyWalletIdentity_IsRejected()
    {
        var f = Fixture();
        f.Wallet.UserId = Guid.NewGuid();
        Assert.Throws<InvalidOperationException>(() =>
            EscrowSettlementPreconditions.EnsureCustomerBacking(f.Hold, f.Wallet, f.ErrandId, f.CustomerId));
        f.Wallet.UserId = f.CustomerId;
        f.Wallet.Id = Guid.Empty;
        Assert.Throws<InvalidOperationException>(() =>
            EscrowSettlementPreconditions.EnsureCustomerBacking(f.Hold, f.Wallet, f.ErrandId, f.CustomerId));
    }

    [Fact]
    public void InsufficientLockedBalance_IsRejected()
    {
        var f = Fixture();
        f.Wallet.LockedBalance = 19.99m;
        Assert.Throws<InvalidOperationException>(() =>
            EscrowSettlementPreconditions.EnsureCustomerBacking(f.Hold, f.Wallet, f.ErrandId, f.CustomerId));
    }

    [Fact]
    public void InactiveOrWrongCurrencyCustomerWallet_IsRejected()
    {
        var f = Fixture();
        f.Wallet.IsActive = false;
        Assert.Throws<InvalidOperationException>(() =>
            EscrowSettlementPreconditions.EnsureCustomerBacking(f.Hold, f.Wallet, f.ErrandId, f.CustomerId));
        f.Wallet.IsActive = true;
        f.Wallet.Currency = "USD";
        Assert.Throws<InvalidOperationException>(() =>
            EscrowSettlementPreconditions.EnsureCustomerBacking(f.Hold, f.Wallet, f.ErrandId, f.CustomerId));
    }

    [Fact]
    public void AlreadySettledHold_IsRejected()
    {
        var f = Fixture();
        f.Hold.MarkRefunded(20m, new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc));
        Assert.Throws<InvalidOperationException>(() =>
            EscrowSettlementPreconditions.EnsureCustomerBacking(f.Hold, f.Wallet, f.ErrandId, f.CustomerId));
    }

    [Fact]
    public void ConfiguredTreasuryWithMatchingActiveNgnWallet_IsAccepted()
    {
        var treasuryId = Guid.NewGuid();
        EscrowSettlementPreconditions.EnsureCommissionDestination(1m, treasuryId, TreasuryWallet(treasuryId));
    }

    [Fact]
    public void ZeroCommission_DoesNotRequireTreasury()
    {
        EscrowSettlementPreconditions.EnsureCommissionDestination(0m, null, null);
    }

    [Fact]
    public void Commission_RequiresExplicitTreasuryAndMatchingWallet()
    {
        var treasuryId = Guid.NewGuid();
        var wallet = TreasuryWallet(treasuryId);
        Assert.Throws<InvalidOperationException>(() => EscrowSettlementPreconditions.EnsureCommissionDestination(1m, null, wallet));
        Assert.Throws<InvalidOperationException>(() => EscrowSettlementPreconditions.EnsureCommissionDestination(1m, Guid.Empty, wallet));
        Assert.Throws<InvalidOperationException>(() => EscrowSettlementPreconditions.EnsureCommissionDestination(1m, treasuryId, null));
        wallet.UserId = Guid.NewGuid();
        Assert.Throws<InvalidOperationException>(() => EscrowSettlementPreconditions.EnsureCommissionDestination(1m, treasuryId, wallet));
    }

    [Fact]
    public void Commission_RejectsInactiveOrWrongCurrencyTreasuryWallet()
    {
        var treasuryId = Guid.NewGuid();
        var wallet = TreasuryWallet(treasuryId);
        wallet.IsActive = false;
        Assert.Throws<InvalidOperationException>(() => EscrowSettlementPreconditions.EnsureCommissionDestination(1m, treasuryId, wallet));
        wallet.IsActive = true;
        wallet.Currency = "USD";
        Assert.Throws<InvalidOperationException>(() => EscrowSettlementPreconditions.EnsureCommissionDestination(1m, treasuryId, wallet));
    }

    [Fact]
    public void Commission_RejectsNegativeOrFractionalKobo()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => EscrowSettlementPreconditions.EnsureCommissionDestination(-1m, null, null));
        Assert.Throws<ArgumentOutOfRangeException>(() => EscrowSettlementPreconditions.EnsureCommissionDestination(0.004m, null, null));
    }

    private static (EscrowHold Hold, Wallet Wallet, Guid ErrandId, Guid CustomerId) Fixture()
    {
        var customer = Guid.NewGuid();
        var errand = Guid.NewGuid();
        var hold = EscrowHold.Create(errand, customer, 20m);
        var wallet = new Wallet { Id = Guid.NewGuid(), UserId = customer, Balance = 80m, LockedBalance = 20m, Currency = "NGN", IsActive = true };
        return (hold, wallet, errand, customer);
    }

    private static Wallet TreasuryWallet(Guid owner) => new()
    {
        Id = Guid.NewGuid(), UserId = owner, Currency = "NGN", IsActive = true
    };
}
