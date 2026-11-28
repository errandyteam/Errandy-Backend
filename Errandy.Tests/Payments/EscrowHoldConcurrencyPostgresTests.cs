using System;
using System.Linq;
using System.Security.Cryptography;
using System.Threading.Tasks;
using Errandy.Domain.Entities;
using Errandy.Domain.Enums;
using Errandy.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Xunit;
using TransactionEntity = Errandy.Domain.Entities.Transaction;
using WalletTransactionStatus = Errandy.Domain.Enums.TransactionStatus;

namespace Errandy.Tests.Payments;

// Synthetic fixtures in the dedicated local test DB only. This exercises EF
// persistence directly, not the disabled EscrowService or an external payout.
public class EscrowHoldConcurrencyPostgresTests : IAsyncLifetime
{
    private readonly string _connection = GetSafeTestConnection();
    private readonly Guid _userId = Guid.NewGuid();
    private readonly Guid _walletId = Guid.NewGuid();
    private readonly DateTime _now = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
    private Guid _errandId;
    private Guid _holdId;

    public async Task InitializeAsync()
    {
        await using var db = CreateContext();
        var phone = RandomNumberGenerator.GetInt32(10_000_000, 100_000_000);
        db.Users.Add(new User
        {
            Id = _userId, FullName = "Escrow Concurrency Test", Email = $"{_userId:N}@tests.invalid",
            PhoneNumber = "080" + phone.ToString(), PasswordHash = "test-only-not-a-real-password", Role = UserRole.Customer
        });
        db.Wallets.Add(new Wallet
        {
            Id = _walletId, UserId = _userId, Balance = 80m, LockedBalance = 20m,
            Currency = "NGN", IsActive = true
        });
        await db.SaveChangesAsync();
        var errand = Errand.Create(_userId, Enum.GetValues<ErrandCategory>()[0],
            "Synthetic escrow concurrency test", 20m, 0, 0, ErrandTimePreference.Asap, null, _now);
        _errandId = errand.Id;
        db.Errands.Add(errand);
        await db.SaveChangesAsync();
        var hold = EscrowHold.Create(_errandId, _userId, 20m);
        db.EscrowHolds.Add(hold);
        await db.SaveChangesAsync();
        _holdId = hold.Id;
        db.Transactions.Add(new TransactionEntity
        {
            WalletId = _walletId, ErrandId = _errandId, Type = TransactionType.Debit,
            Category = TransactionCategory.EscrowLock, Amount = 20m, BalanceBefore = 100m,
            BalanceAfter = 80m, Status = WalletTransactionStatus.Completed,
            Reference = TransactionEntity.GenerateReference("ESC-TEST-LOCK"), Description = "Synthetic escrow lock"
        });
        await db.SaveChangesAsync();
    }

    public async Task DisposeAsync()
    {
        await using var db = CreateContext();
        db.EscrowHolds.RemoveRange(await db.EscrowHolds.Where(h => h.Id == _holdId && h.ErrandId == _errandId).ToListAsync());
        await db.SaveChangesAsync();
        db.Transactions.RemoveRange(await db.Transactions.Where(t => t.WalletId == _walletId && t.ErrandId == _errandId).ToListAsync());
        await db.SaveChangesAsync();
        var errand = await db.Errands.SingleOrDefaultAsync(e => e.Id == _errandId && e.CustomerId == _userId);
        if (errand is not null) { db.Errands.Remove(errand); await db.SaveChangesAsync(); }
        var wallet = await db.Wallets.SingleOrDefaultAsync(w => w.Id == _walletId && w.UserId == _userId);
        if (wallet is not null) { db.Wallets.Remove(wallet); await db.SaveChangesAsync(); }
        var user = await db.Users.SingleOrDefaultAsync(u => u.Id == _userId);
        if (user is not null) { db.Users.Remove(user); await db.SaveChangesAsync(); }
    }

    [Fact]
    public async Task StaleHoldAndFreshWallet_RollsBackSecondRefundAndLedgerEntry()
    {
        await using (var first = CreateContext())
        await using (var second = CreateContext())
        {
            var firstHold = await first.EscrowHolds.SingleAsync(h => h.Id == _holdId);
            var staleHold = await second.EscrowHolds.SingleAsync(h => h.Id == _holdId);
            var firstWallet = await first.Wallets.SingleAsync(w => w.Id == _walletId);
            Assert.Equal(EscrowHoldStatus.Held, firstHold.Status);
            Assert.Equal(EscrowHoldStatus.Held, staleHold.Status);
            Assert.Equal(80m, firstWallet.Balance);
            Assert.Equal(20m, firstWallet.LockedBalance);

            firstWallet.RefundLockedFunds(20m);
            firstHold.MarkRefunded(20m, _now);
            first.Transactions.Add(RefundEntry(80m, 100m));
            await first.SaveChangesAsync();

            // B has a stale hold, but deliberately reads the wallet only after A
            // committed. The old unsafe refund fallback would credit it again.
            var freshWallet = await second.Wallets.SingleAsync(w => w.Id == _walletId);
            Assert.Equal(100m, freshWallet.Balance);
            Assert.Equal(0m, freshWallet.LockedBalance);
            freshWallet.Credit(20m);
            staleHold.MarkRefunded(20m, _now);
            second.Transactions.Add(RefundEntry(100m, 120m));
            await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => second.SaveChangesAsync());
        }

        await using var verify = CreateContext();
        var wallet = await verify.Wallets.AsNoTracking().SingleAsync(w => w.Id == _walletId);
        var hold = await verify.EscrowHolds.AsNoTracking().SingleAsync(h => h.Id == _holdId);
        var refunds = await verify.Transactions.AsNoTracking().Where(t =>
            t.WalletId == _walletId && t.ErrandId == _errandId && t.Category == TransactionCategory.EscrowRefund).ToListAsync();
        Assert.Equal(100m, wallet.Balance);
        Assert.Equal(0m, wallet.LockedBalance);
        Assert.Equal(EscrowHoldStatus.Refunded, hold.Status);
        Assert.Equal(0m, hold.CurrentLockedAmount);
        Assert.Equal(20m, Assert.Single(refunds).Amount);
    }

    private TransactionEntity RefundEntry(decimal before, decimal after) => new()
    {
        WalletId = _walletId, ErrandId = _errandId, Type = TransactionType.Credit,
        Category = TransactionCategory.EscrowRefund, Amount = 20m,
        BalanceBefore = before, BalanceAfter = after, Status = WalletTransactionStatus.Completed,
        Reference = TransactionEntity.GenerateReference("ESC-TEST-REF"), Description = "Synthetic escrow refund"
    };

    private ErrandyDbContext CreateContext() => new(new DbContextOptionsBuilder<ErrandyDbContext>().UseNpgsql(_connection).Options);
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
