using System;
using System.Data.Common;
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
using Microsoft.EntityFrameworkCore.Diagnostics;
using Npgsql;
using Xunit;
using TransactionEntity = Errandy.Domain.Entities.Transaction;
using WalletTransactionStatus = Errandy.Domain.Enums.TransactionStatus;

namespace Errandy.Tests.Payments;

// Synthetic committed fixtures in the guarded test DB, cleaned up afterwards.
// This tests the UNWIRED preparation and EF saves, not the disabled service,
// caller authorization, a dispute handler, external payments, or all races.
public class EscrowFullRefundPreparationRacePostgresTests : IAsyncLifetime
{
    private readonly string _connection = GetSafeTestConnection();
    private readonly Guid _userId = Guid.NewGuid();
    private readonly Guid _walletId = Guid.NewGuid();
    private readonly FixedClock _clock = new();
    private Guid _firstErrandId;
    private Guid _secondErrandId;
    private Guid _firstHoldId;
    private Guid _secondHoldId;

    public async Task InitializeAsync()
    {
        await using var db = CreateContext();
        var phone = RandomNumberGenerator.GetInt32(10_000_000, 100_000_000);
        db.Users.Add(new User
        {
            Id = _userId,
            FullName = "Escrow Refund Race",
            Email = $"{_userId:N}@tests.invalid",
            PhoneNumber = "080" + phone.ToString(),
            PasswordHash = "test-only-not-a-real-password",
            Role = UserRole.Customer
        });
        db.Wallets.Add(new Wallet
        {
            Id = _walletId,
            UserId = _userId,
            Balance = 60m,
            LockedBalance = 40m,
            Currency = "NGN",
            IsActive = true
        });
        await db.SaveChangesAsync();

        var firstErrand = NewErrand();
        var secondErrand = NewErrand();
        _firstErrandId = firstErrand.Id;
        _secondErrandId = secondErrand.Id;
        db.Errands.AddRange(firstErrand, secondErrand);
        await db.SaveChangesAsync();
        var firstHold = EscrowHold.Create(_firstErrandId, _userId, 20m);
        var secondHold = EscrowHold.Create(_secondErrandId, _userId, 20m);
        db.EscrowHolds.AddRange(firstHold, secondHold);
        await db.SaveChangesAsync();
        _firstHoldId = firstHold.Id;
        _secondHoldId = secondHold.Id;
        db.Transactions.AddRange(LockEntry(_firstErrandId, 100m, 80m), LockEntry(_secondErrandId, 80m, 60m));
        await db.SaveChangesAsync();
    }

    public async Task DisposeAsync()
    {
        await using var db = CreateContext();
        db.EscrowHolds.RemoveRange(await db.EscrowHolds.Where(h => h.CustomerId == _userId &&
            (h.Id == _firstHoldId || h.Id == _secondHoldId)).ToListAsync());
        await db.SaveChangesAsync();
        db.Transactions.RemoveRange(await db.Transactions.Where(t => t.WalletId == _walletId).ToListAsync());
        await db.SaveChangesAsync();
        db.Errands.RemoveRange(await db.Errands.Where(e => e.CustomerId == _userId &&
            (e.Id == _firstErrandId || e.Id == _secondErrandId)).ToListAsync());
        await db.SaveChangesAsync();
        var wallet = await db.Wallets.SingleOrDefaultAsync(w => w.Id == _walletId && w.UserId == _userId);
        if (wallet is not null) { db.Wallets.Remove(wallet); await db.SaveChangesAsync(); }
        var user = await db.Users.SingleOrDefaultAsync(u => u.Id == _userId);
        if (user is not null) { db.Users.Remove(user); await db.SaveChangesAsync(); }
    }

    [Fact]
    public async Task StaleHoldThenFreshWallet_IsRejectedBeforeTakingOtherHoldsFunds()
    {
        var pause = new PauseBeforeWalletRead();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(25));
        await using var second = CreateContext(pause);
        // B reads the target hold, then pauses before reading the wallet.
        var loser = Preparation(second).PrepareAsync(_firstErrandId, _userId, timeout.Token);
        try
        {
            await pause.Reached.Task.WaitAsync(timeout.Token);
            await using var first = CreateContext();
            await Preparation(first).PrepareAsync(_firstErrandId, _userId, timeout.Token);
            await first.SaveChangesAsync(timeout.Token);
        }
        finally { pause.Continue.TrySetResult(true); }

        var error = await Assert.ThrowsAsync<InvalidOperationException>(async () => await loser);
        Assert.Contains("hold changed", error.Message);
        await AssertSingleRefundAsync();
    }

    [Fact]
    public async Task TwoPreparedRefunds_SecondSaveRollsBackWithoutDoubleCredit()
    {
        await using var first = CreateContext();
        await using var second = CreateContext();
        // Both contexts stage from the same committed state before A saves.
        await Preparation(second).PrepareAsync(_firstErrandId, _userId);
        await Preparation(first).PrepareAsync(_firstErrandId, _userId);
        await first.SaveChangesAsync();
        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => second.SaveChangesAsync());
        await AssertSingleRefundAsync();
    }

    [Fact]
    public async Task OneSave_CommitsPreparedRefundAndErrandChangeTogether()
    {
        await using (var db = CreateContext())
        {
            await Preparation(db).PrepareAsync(_firstErrandId, _userId);
            var errand = await db.Errands.SingleAsync(e => e.Id == _firstErrandId);
            errand.Cancel(_clock.UtcNow);
            await db.SaveChangesAsync();
        }
        await AssertSingleRefundAsync();
        await using var verify = CreateContext();
        Assert.Equal(ErrandStatus.Cancelled,
            (await verify.Errands.AsNoTracking().SingleAsync(e => e.Id == _firstErrandId)).Status);
    }

    [Fact]
    public async Task OneSave_WithLedgerFailure_RollsBackRefundAndErrandChange()
    {
        await using (var db = CreateContext())
        {
            await Preparation(db).PrepareAsync(_firstErrandId, _userId);
            var errand = await db.Errands.SingleAsync(e => e.Id == _firstErrandId);
            errand.Cancel(_clock.UtcNow);
            var existingReference = await db.Transactions.AsNoTracking()
                .Where(t => t.WalletId == _walletId && t.ErrandId == _firstErrandId && t.Category == TransactionCategory.EscrowLock)
                .Select(t => t.Reference).SingleAsync();
            // Force a unique-reference violation in the SAME SaveChanges call.
            db.Transactions.Add(new TransactionEntity
            {
                WalletId = _walletId,
                ErrandId = _firstErrandId,
                Type = TransactionType.Debit,
                Category = TransactionCategory.EscrowLock,
                Amount = 1m,
                BalanceBefore = 60m,
                BalanceAfter = 60m,
                Status = WalletTransactionStatus.Completed,
                Reference = existingReference,
                Description = "Synthetic duplicate reference"
            });
            var error = await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
            var postgres = Assert.IsType<PostgresException>(error.InnerException);
            Assert.Equal(PostgresErrorCodes.UniqueViolation, postgres.SqlState);
            Assert.Equal("IX_Transactions_Reference", postgres.ConstraintName);
        } // Dispose the failed context before inspecting or retrying.

        await using var verify = CreateContext();
        var wallet = await verify.Wallets.AsNoTracking().SingleAsync(w => w.Id == _walletId);
        var firstHold = await verify.EscrowHolds.AsNoTracking().SingleAsync(h => h.Id == _firstHoldId);
        var secondHold = await verify.EscrowHolds.AsNoTracking().SingleAsync(h => h.Id == _secondHoldId);
        var errandAfterFailure = await verify.Errands.AsNoTracking().SingleAsync(e => e.Id == _firstErrandId);
        Assert.Equal(60m, wallet.Balance);
        Assert.Equal(40m, wallet.LockedBalance);
        Assert.Equal(EscrowHoldStatus.Held, firstHold.Status);
        Assert.Equal(20m, firstHold.CurrentLockedAmount);
        Assert.Equal(EscrowHoldStatus.Held, secondHold.Status);
        Assert.Equal(20m, secondHold.CurrentLockedAmount);
        Assert.Equal(ErrandStatus.Created, errandAfterFailure.Status);
        Assert.Equal(0, await verify.Transactions.AsNoTracking().CountAsync(t =>
            t.WalletId == _walletId && t.Category == TransactionCategory.EscrowRefund));
        Assert.Equal(2, await verify.Transactions.AsNoTracking().CountAsync(t => t.WalletId == _walletId));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ExplicitTransaction_RollsBackRefundAndErrandOnLaterFailure(bool saveErrandBeforeFailure)
    {
        // This models an orchestration boundary. It does not invoke the real
        // dispute/cancellation handlers or prove they use this transaction.
        await using (var db = CreateContext())
        {
            await using var transaction = await db.Database.BeginTransactionAsync();
            try
            {
                await Preparation(db).PrepareAsync(_firstErrandId, _userId);
                await db.SaveChangesAsync();
                if (saveErrandBeforeFailure)
                {
                    var errand = await db.Errands.SingleAsync(e => e.Id == _firstErrandId);
                    errand.Cancel(_clock.UtcNow);
                    await db.SaveChangesAsync();
                }
                throw new SimulatedLaterFailureException();
            }
            catch (SimulatedLaterFailureException)
            {
                await transaction.RollbackAsync();
            }
        } // Never reuse the context after rollback.

        await using var verify = CreateContext();
        var wallet = await verify.Wallets.AsNoTracking().SingleAsync(w => w.Id == _walletId);
        var firstHold = await verify.EscrowHolds.AsNoTracking().SingleAsync(h => h.Id == _firstHoldId);
        var secondHold = await verify.EscrowHolds.AsNoTracking().SingleAsync(h => h.Id == _secondHoldId);
        var errandAfterRollback = await verify.Errands.AsNoTracking().SingleAsync(e => e.Id == _firstErrandId);
        var refundCount = await verify.Transactions.AsNoTracking().CountAsync(t =>
            t.WalletId == _walletId && t.Category == TransactionCategory.EscrowRefund);
        Assert.Equal(60m, wallet.Balance);
        Assert.Equal(40m, wallet.LockedBalance);
        Assert.Equal(EscrowHoldStatus.Held, firstHold.Status);
        Assert.Equal(20m, firstHold.CurrentLockedAmount);
        Assert.Equal(EscrowHoldStatus.Held, secondHold.Status);
        Assert.Equal(20m, secondHold.CurrentLockedAmount);
        Assert.Equal(ErrandStatus.Created, errandAfterRollback.Status);
        Assert.Equal(0, refundCount);
    }

    private sealed class SimulatedLaterFailureException : Exception { }

    private async Task AssertSingleRefundAsync()
    {
        await using var verify = CreateContext();
        var wallet = await verify.Wallets.AsNoTracking().SingleAsync(w => w.Id == _walletId);
        var first = await verify.EscrowHolds.AsNoTracking().SingleAsync(h => h.Id == _firstHoldId);
        var second = await verify.EscrowHolds.AsNoTracking().SingleAsync(h => h.Id == _secondHoldId);
        var refunds = await verify.Transactions.AsNoTracking().Where(t => t.WalletId == _walletId &&
            t.Category == TransactionCategory.EscrowRefund).ToListAsync();
        Assert.Equal(80m, wallet.Balance);
        Assert.Equal(20m, wallet.LockedBalance);
        Assert.Equal(EscrowHoldStatus.Refunded, first.Status);
        Assert.Equal(0m, first.CurrentLockedAmount);
        Assert.Equal(EscrowHoldStatus.Held, second.Status);
        Assert.Equal(20m, second.CurrentLockedAmount);
        var refund = Assert.Single(refunds);
        Assert.Equal((Guid?)_firstErrandId, refund.ErrandId);
        Assert.Equal(20m, refund.Amount);
        Assert.Equal(60m, refund.BalanceBefore);
        Assert.Equal(80m, refund.BalanceAfter);
    }

    private EscrowFullRefundPreparation Preparation(ErrandyDbContext db) => new(db, _clock);
    private Errand NewErrand() => Errand.Create(_userId, Enum.GetValues<ErrandCategory>()[0],
        "Synthetic escrow refund race", 20m, 0, 0, ErrandTimePreference.Asap, null, _clock.UtcNow);
    private TransactionEntity LockEntry(Guid errandId, decimal before, decimal after) => new()
    {
        WalletId = _walletId,
        ErrandId = errandId,
        Type = TransactionType.Debit,
        Category = TransactionCategory.EscrowLock,
        Amount = 20m,
        BalanceBefore = before,
        BalanceAfter = after,
        Status = WalletTransactionStatus.Completed,
        Reference = TransactionEntity.GenerateReference("ESC-TEST-LOCK"),
        Description = "Synthetic escrow lock"
    };
    private ErrandyDbContext CreateContext(DbCommandInterceptor? interceptor = null)
    {
        var builder = new DbContextOptionsBuilder<ErrandyDbContext>().UseNpgsql(_connection);
        if (interceptor is not null) builder.AddInterceptors(interceptor);
        return new ErrandyDbContext(builder.Options);
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

    private sealed class FixedClock : IDateTimeProvider
    {
        public DateTime UtcNow => new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
    }

    private sealed class PauseBeforeWalletRead : DbCommandInterceptor
    {
        private int _paused;
        public TaskCompletionSource<bool> Reached { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource<bool> Continue { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public override async ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result,
            CancellationToken cancellationToken = default)
        {
            if (command.CommandText.Contains("\"Wallets\"", StringComparison.Ordinal) &&
                command.CommandText.TrimStart().StartsWith("SELECT", StringComparison.OrdinalIgnoreCase) &&
                Interlocked.Exchange(ref _paused, 1) == 0)
            {
                Reached.TrySetResult(true);
                await Continue.Task.WaitAsync(cancellationToken);
            }
            return result;
        }
    }
}
