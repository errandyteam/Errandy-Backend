using System;
using System.Linq;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using Errandy.Application.Interfaces;
using Errandy.Domain.Entities;
using Errandy.Domain.Enums;
using Errandy.Infrastructure.Persistence;
using Errandy.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;
using Xunit;
using TransactionEntity = Errandy.Domain.Entities.Transaction;
using WalletTransactionStatus = Errandy.Domain.Enums.TransactionStatus;

namespace Errandy.Tests.Payments;

// Synthetic data in a rolled-back transaction on the dedicated test DB only.
// This exercises the unwired preparation, not EscrowService or a real payout.
public class EscrowFullRefundPreparationPostgresTests : IAsyncLifetime
{
    private readonly string _connection = GetSafeTestConnection();
    private readonly Guid _userId = Guid.NewGuid();
    private readonly Guid _walletId = Guid.NewGuid();
    private readonly FixedClock _clock = new();
    private ErrandyDbContext _db = null!;
    private IDbContextTransaction _tx = null!;
    private Guid _errandId;
    private Guid _holdId;

    public async Task InitializeAsync()
    {
        _db = new ErrandyDbContext(new DbContextOptionsBuilder<ErrandyDbContext>().UseNpgsql(_connection).Options);
        _tx = await _db.Database.BeginTransactionAsync();
        var phone = RandomNumberGenerator.GetInt32(10_000_000, 100_000_000);
        _db.Users.Add(new User
        {
            Id = _userId, FullName = "Refund Preparation Test", Email = $"{_userId:N}@tests.invalid",
            PhoneNumber = "080" + phone.ToString(), PasswordHash = "test-only-not-a-real-password", Role = UserRole.Customer
        });
        _db.Wallets.Add(new Wallet
        {
            Id = _walletId, UserId = _userId, Balance = 80m, LockedBalance = 20m, Currency = "NGN", IsActive = true
        });
        await _db.SaveChangesAsync();
        var errand = NewErrand();
        _errandId = errand.Id;
        _db.Errands.Add(errand);
        await _db.SaveChangesAsync();
        var hold = EscrowHold.Create(_errandId, _userId, 20m);
        _db.EscrowHolds.Add(hold);
        await _db.SaveChangesAsync();
        _holdId = hold.Id;
        _db.Transactions.Add(new TransactionEntity
        {
            WalletId = _walletId, ErrandId = _errandId, Type = TransactionType.Debit,
            Category = TransactionCategory.EscrowLock, Amount = 20m, BalanceBefore = 100m,
            BalanceAfter = 80m, Status = WalletTransactionStatus.Completed,
            Reference = TransactionEntity.GenerateReference("ESC-TEST-LOCK"), Description = "Synthetic escrow lock"
        });
        await _db.SaveChangesAsync();
        _db.ChangeTracker.Clear();
    }

    public async Task DisposeAsync()
    {
        if (_tx is not null) { await _tx.RollbackAsync(); await _tx.DisposeAsync(); }
        if (_db is not null) await _db.DisposeAsync();
    }

    [Fact]
    public async Task FullRefund_IsStagedAndNotPersistedUntilCallerSaves()
    {
        await Preparation().PrepareAsync(_errandId, _userId);
        var originalWallet = await _db.Wallets.AsNoTracking().SingleAsync(w => w.Id == _walletId);
        var originalHold = await _db.EscrowHolds.AsNoTracking().SingleAsync(h => h.Id == _holdId);
        Assert.Equal(80m, originalWallet.Balance);
        Assert.Equal(20m, originalWallet.LockedBalance);
        Assert.Equal(EscrowHoldStatus.Held, originalHold.Status);
        Assert.Equal(0, await RefundCount());

        await _db.SaveChangesAsync();
        _db.ChangeTracker.Clear();
        var wallet = await _db.Wallets.AsNoTracking().SingleAsync(w => w.Id == _walletId);
        var hold = await _db.EscrowHolds.AsNoTracking().SingleAsync(h => h.Id == _holdId);
        var refund = await _db.Transactions.AsNoTracking().SingleAsync(t =>
            t.WalletId == _walletId && t.ErrandId == _errandId && t.Category == TransactionCategory.EscrowRefund);
        Assert.Equal(100m, wallet.Balance);
        Assert.Equal(0m, wallet.LockedBalance);
        Assert.Equal(EscrowHoldStatus.Refunded, hold.Status);
        Assert.Equal(0m, hold.CurrentLockedAmount);
        Assert.Equal(20m, hold.RefundedToCustomerAmount);
        Assert.Equal(20m, refund.Amount);
        Assert.Equal(80m, refund.BalanceBefore);
        Assert.Equal(100m, refund.BalanceAfter);
        Assert.Equal(TransactionType.Credit, refund.Type);
        Assert.Equal(WalletTransactionStatus.Completed, refund.Status);
    }

    [Fact]
    public async Task MissingHold_IsRejectedWithoutPreparingARefund()
    {
        await Assert.ThrowsAsync<InvalidOperationException>(() => Preparation().PrepareAsync(Guid.NewGuid(), _userId));
        await AssertOriginalAsync();
    }

    [Fact]
    public async Task WrongCustomer_IsRejectedWithoutPreparingARefund()
    {
        await Assert.ThrowsAsync<InvalidOperationException>(() => Preparation().PrepareAsync(_errandId, Guid.NewGuid()));
        await AssertOriginalAsync();
    }

    [Fact]
    public async Task InsufficientLockedFunds_AreRejectedWithoutPreparingARefund()
    {
        var wallet = await _db.Wallets.SingleAsync(w => w.Id == _walletId);
        wallet.LockedBalance = 19m;
        await _db.SaveChangesAsync();
        _db.ChangeTracker.Clear();
        await Assert.ThrowsAsync<InvalidOperationException>(() => Preparation().PrepareAsync(_errandId, _userId));
        Assert.Equal(0, await RefundCount());
        Assert.Equal(EscrowHoldStatus.Held, (await _db.EscrowHolds.AsNoTracking().SingleAsync(h => h.Id == _holdId)).Status);
        Assert.Equal(19m, (await _db.Wallets.AsNoTracking().SingleAsync(w => w.Id == _walletId)).LockedBalance);
    }

    [Fact]
    public async Task WalletMustBackAllActiveHolds_NotJustTheTarget()
    {
        var otherErrand = NewErrand();
        _db.Errands.Add(otherErrand);
        await _db.SaveChangesAsync();
        _db.EscrowHolds.Add(EscrowHold.Create(otherErrand.Id, _userId, 20m));
        await _db.SaveChangesAsync();
        _db.ChangeTracker.Clear();
        await Assert.ThrowsAsync<InvalidOperationException>(() => Preparation().PrepareAsync(_errandId, _userId));
        await AssertOriginalAsync();
    }

    private async Task AssertOriginalAsync()
    {
        var wallet = await _db.Wallets.AsNoTracking().SingleAsync(w => w.Id == _walletId);
        var hold = await _db.EscrowHolds.AsNoTracking().SingleAsync(h => h.Id == _holdId);
        Assert.Equal(80m, wallet.Balance);
        Assert.Equal(20m, wallet.LockedBalance);
        Assert.Equal(EscrowHoldStatus.Held, hold.Status);
        Assert.Equal(0, await RefundCount());
    }

    private Task<int> RefundCount() => _db.Transactions.AsNoTracking().CountAsync(t =>
        t.WalletId == _walletId && t.ErrandId == _errandId && t.Category == TransactionCategory.EscrowRefund);
    private EscrowFullRefundPreparation Preparation() => new(_db, _clock);
    private Errand NewErrand() => Errand.Create(_userId, Enum.GetValues<ErrandCategory>()[0],
        "Synthetic refund preparation", 20m, 0, 0, ErrandTimePreference.Asap, null, _clock.UtcNow);
    private sealed class FixedClock : IDateTimeProvider
    {
        public DateTime UtcNow => new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
    }
    private static string GetSafeTestConnection()
    {
        var connection = Environment.GetEnvironmentVariable("ERRANDY_PAYMENT_TEST_CONNECTION");
        if (string.IsNullOrWhiteSpace(connection)) throw new InvalidOperationException("Set ERRANDY_PAYMENT_TEST_CONNECTION for the dedicated local test database.");
        NpgsqlConnectionStringBuilder parsed;
        try { parsed = new NpgsqlConnectionStringBuilder(connection); }
        catch (ArgumentException) { throw new InvalidOperationException("The test connection string is invalid."); }
        if (!string.Equals(parsed.Host, "localhost", StringComparison.OrdinalIgnoreCase) || parsed.Port != 5432 ||
            !string.Equals(parsed.Database, "errandy_payment_tests", StringComparison.Ordinal))
            throw new InvalidOperationException("Tests are restricted to localhost:5432/errandy_payment_tests.");
        return connection;
    }
}
