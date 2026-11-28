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

namespace Errandy.Tests.Payments;

// These tests use only the dedicated local test database. They do not start a
// hosted worker, run Program.cs, contact Paystack, or test wallet idempotency.
public class AutomaticPaymentCheckPostgresTests : IAsyncLifetime
{
    private readonly string _connection = GetSafeTestConnection();
    private readonly Guid _customerId = Guid.NewGuid();
    private readonly Guid _walletId = Guid.NewGuid();
    private readonly string _reference = $"PAY-{Guid.NewGuid():N}";
    private readonly DateTime _due = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
    private Guid _intentId;

    public async Task InitializeAsync()
    {
        await using var db = CreateContext();
        var phone = RandomNumberGenerator.GetInt32(10_000_000, 99_999_999);
        db.Users.Add(new User
        {
            Id = _customerId, FullName = "Automatic Check Test Customer", Email = $"{_customerId:N}@tests.invalid",
            PhoneNumber = "080" + phone.ToString(), PasswordHash = "test-only-not-a-real-password", Role = UserRole.Customer
        });
        db.Wallets.Add(new Wallet
        {
            Id = _walletId, UserId = _customerId, Balance = 100m, LockedBalance = 0m,
            Currency = "NGN", IsActive = true
        });
        var intent = PaymentIntent.Create(_customerId, _walletId, 50m, _reference,
            $"{_customerId:N}@tests.invalid", "Paystack");
        _intentId = intent.Id;
        db.PaymentIntents.Add(intent);
        await db.SaveChangesAsync();
    }

    public async Task DisposeAsync()
    {
        // Delete only rows associated with this test instance, in FK order.
        await using var db = CreateContext();
        var checks = await db.AutomaticPaymentChecks.Where(c => c.PaymentIntentId == _intentId).ToListAsync();
        db.AutomaticPaymentChecks.RemoveRange(checks);
        await db.SaveChangesAsync();
        var intent = await db.PaymentIntents.SingleOrDefaultAsync(p => p.Id == _intentId && p.UserId == _customerId);
        if (intent is not null) { db.PaymentIntents.Remove(intent); await db.SaveChangesAsync(); }
        var wallet = await db.Wallets.SingleOrDefaultAsync(w => w.Id == _walletId && w.UserId == _customerId);
        if (wallet is not null) { db.Wallets.Remove(wallet); await db.SaveChangesAsync(); }
        var user = await db.Users.SingleOrDefaultAsync(u => u.Id == _customerId);
        if (user is not null) { db.Users.Remove(user); await db.SaveChangesAsync(); }
    }

    [Fact]
    public void Revision_IsConfiguredAsConcurrencyToken()
    {
        using var db = CreateContext();
        var entity = db.Model.FindEntityType(typeof(AutomaticPaymentCheck));
        Assert.NotNull(entity);
        var revision = entity!.FindProperty(nameof(AutomaticPaymentCheck.Revision));
        Assert.NotNull(revision);
        Assert.True(revision!.IsConcurrencyToken);
    }

    [Fact]
    public async Task Schedule_RoundTripsAndDoesNotModifyPaymentOrWallet()
    {
        var id = await InsertCheckAsync();
        await using var db = CreateContext();
        var check = await db.AutomaticPaymentChecks.AsNoTracking().SingleAsync(c => c.Id == id);
        Assert.Equal(_intentId, check.PaymentIntentId);
        Assert.Equal(AutomaticPaymentCheckStatus.Scheduled, check.Status);
        Assert.Equal(AutomaticPaymentCheckResult.None, check.LastResult);
        Assert.Equal((DateTime?)_due, check.NextAttemptAt);
        Assert.Equal(0, check.AttemptsStarted);
        Assert.Equal(10, check.MaximumAttempts);
        Assert.Equal(0L, check.Revision);
        Assert.Equal(PaymentStatus.Pending, (await db.PaymentIntents.AsNoTracking().SingleAsync(p => p.Id == _intentId)).Status);
        Assert.Equal(100m, (await db.Wallets.AsNoTracking().SingleAsync(w => w.Id == _walletId)).Balance);
        Assert.False(await db.Transactions.AsNoTracking().AnyAsync(t => t.WalletId == _walletId));
    }

    [Fact]
    public async Task DuplicatePaymentCheck_IsRejectedByUniqueIndex()
    {
        var firstId = await InsertCheckAsync();
        await using (var db = CreateContext())
        {
            db.AutomaticPaymentChecks.Add(AutomaticPaymentCheck.Schedule(_intentId, _due, 10));
            var error = await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
            var postgres = Assert.IsType<PostgresException>(error.InnerException);
            Assert.Equal(PostgresErrorCodes.UniqueViolation, postgres.SqlState);
            Assert.Equal("IX_AutomaticPaymentChecks_PaymentIntentId", postgres.ConstraintName);
        }
        await using var verify = CreateContext();
        var stored = await verify.AutomaticPaymentChecks.AsNoTracking().SingleAsync(c => c.PaymentIntentId == _intentId);
        Assert.Equal(firstId, stored.Id);
    }

    [Fact]
    public async Task CheckWithMissingPayment_IsRejectedByForeignKey()
    {
        await using var db = CreateContext();
        db.AutomaticPaymentChecks.Add(AutomaticPaymentCheck.Schedule(Guid.NewGuid(), _due, 10));
        var error = await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
        var postgres = Assert.IsType<PostgresException>(error.InnerException);
        Assert.Equal(PostgresErrorCodes.ForeignKeyViolation, postgres.SqlState);
        Assert.Equal("FK_AutomaticPaymentChecks_PaymentIntents_PaymentIntentId", postgres.ConstraintName);
    }

    [Fact]
    public async Task ExistingCheck_RestrictsDeletingPayment()
    {
        await InsertCheckAsync();
        await using (var db = CreateContext())
        {
            db.PaymentIntents.Remove(await db.PaymentIntents.SingleAsync(p => p.Id == _intentId));
            var error = await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
            var postgres = Assert.IsType<PostgresException>(error.InnerException);
            Assert.True(postgres.SqlState == "23001" || postgres.SqlState == PostgresErrorCodes.ForeignKeyViolation);
            Assert.Equal("FK_AutomaticPaymentChecks_PaymentIntents_PaymentIntentId", postgres.ConstraintName);
        }
        await using var verify = CreateContext();
        Assert.True(await verify.PaymentIntents.AsNoTracking().AnyAsync(p => p.Id == _intentId));
        Assert.True(await verify.AutomaticPaymentChecks.AsNoTracking().AnyAsync(c => c.PaymentIntentId == _intentId));
    }

    [Fact]
    public async Task TwoContextsStartingSameCheck_ExactlyOneSaveSucceeds()
    {
        var id = await InsertCheckAsync();
        await using var firstDb = CreateContext();
        await using var secondDb = CreateContext();
        var first = await firstDb.AutomaticPaymentChecks.SingleAsync(c => c.Id == id);
        var second = await secondDb.AutomaticPaymentChecks.SingleAsync(c => c.Id == id);
        var firstToken = Guid.NewGuid();
        var secondToken = Guid.NewGuid();
        Assert.True(first.TryStart(firstToken, _due, TimeSpan.FromMinutes(2)));
        Assert.True(second.TryStart(secondToken, _due, TimeSpan.FromMinutes(2)));

        // Both contexts read the same initial revision before either saves.
        var saves = await Task.WhenAll(TrySaveAsync(firstDb), TrySaveAsync(secondDb));
        Assert.Equal(1, saves.Count(success => success));

        await using var verify = CreateContext();
        var stored = await verify.AutomaticPaymentChecks.AsNoTracking().SingleAsync(c => c.Id == id);
        Assert.Equal(AutomaticPaymentCheckStatus.Leased, stored.Status);
        Assert.Equal((Guid?)(saves[0] ? firstToken : secondToken), stored.LeaseToken);
        Assert.Equal(1, stored.AttemptsStarted);
        Assert.Equal(1L, stored.Revision);
        Assert.Null(stored.NextAttemptAt);
    }

    [Fact]
    public async Task StaleFinalization_CannotOverwriteRecoveredAndReclaimedLease()
    {
        var id = await InsertCheckAsync();
        var oldToken = Guid.NewGuid();
        var newToken = Guid.NewGuid();
        var expiry = _due.AddMinutes(2);
        var retryAt = expiry.AddMinutes(5);
        await using (var db = CreateContext())
        {
            var check = await db.AutomaticPaymentChecks.SingleAsync(c => c.Id == id);
            Assert.True(check.TryStart(oldToken, _due, TimeSpan.FromMinutes(2)));
            await db.SaveChangesAsync();
        }

        // The old worker loaded revision 1 before recovery and is delayed before saving.
        await using var staleDb = CreateContext();
        var stale = await staleDb.AutomaticPaymentChecks.SingleAsync(c => c.Id == id);
        await using (var recoveryDb = CreateContext())
        {
            var current = await recoveryDb.AutomaticPaymentChecks.SingleAsync(c => c.Id == id);
            current.RecoverExpiredLease(expiry, retryAt);
            await recoveryDb.SaveChangesAsync();
        }
        await using (var claimantDb = CreateContext())
        {
            var current = await claimantDb.AutomaticPaymentChecks.SingleAsync(c => c.Id == id);
            Assert.True(current.TryStart(newToken, retryAt, TimeSpan.FromMinutes(2)));
            await claimantDb.SaveChangesAsync();
        }

        // Simulate a worker that prepared its update while its lease was live,
        // but whose database save happened only after another worker reclaimed it.
        stale.MarkConfirmed(oldToken, _due.AddMinutes(1));
        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => staleDb.SaveChangesAsync());

        await using var verify = CreateContext();
        var stored = await verify.AutomaticPaymentChecks.AsNoTracking().SingleAsync(c => c.Id == id);
        Assert.Equal(AutomaticPaymentCheckStatus.Leased, stored.Status);
        Assert.Equal((Guid?)newToken, stored.LeaseToken);
        Assert.Equal(2, stored.AttemptsStarted);
        Assert.Equal(3L, stored.Revision);
        Assert.Equal(AutomaticPaymentCheckResult.LeaseExpired, stored.LastResult);
    }

    [Fact]
    public async Task StaleSave_RollsBackOtherChangesInSameSaveChanges()
    {
        var id = await InsertCheckAsync();
        await using var staleDb = CreateContext();
        await using var winningDb = CreateContext();
        var stale = await staleDb.AutomaticPaymentChecks.SingleAsync(c => c.Id == id);
        var winner = await winningDb.AutomaticPaymentChecks.SingleAsync(c => c.Id == id);
        var staleWallet = await staleDb.Wallets.SingleAsync(w => w.Id == _walletId);
        Assert.True(stale.TryStart(Guid.NewGuid(), _due, TimeSpan.FromMinutes(2)));
        staleWallet.Credit(25m);
        Assert.True(winner.TryStart(Guid.NewGuid(), _due, TimeSpan.FromMinutes(2)));
        await winningDb.SaveChangesAsync();
        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => staleDb.SaveChangesAsync());

        // Dispose the failed scope; inspect with a fresh context, never re-save dirty state.
        await using var verify = CreateContext();
        Assert.Equal(100m, (await verify.Wallets.AsNoTracking().SingleAsync(w => w.Id == _walletId)).Balance);
        Assert.Equal(1, (await verify.AutomaticPaymentChecks.AsNoTracking().SingleAsync(c => c.Id == id)).AttemptsStarted);
    }

    private async Task<Guid> InsertCheckAsync()
    {
        await using var db = CreateContext();
        var check = AutomaticPaymentCheck.Schedule(_intentId, _due, 10);
        db.AutomaticPaymentChecks.Add(check);
        await db.SaveChangesAsync();
        return check.Id;
    }

    private static async Task<bool> TrySaveAsync(ErrandyDbContext db)
    {
        try { await db.SaveChangesAsync(); return true; }
        catch (DbUpdateConcurrencyException) { return false; }
    }

    private ErrandyDbContext CreateContext() => new(new DbContextOptionsBuilder<ErrandyDbContext>().UseNpgsql(_connection).Options);

    private static string GetSafeTestConnection()
    {
        var connection = Environment.GetEnvironmentVariable("ERRANDY_PAYMENT_TEST_CONNECTION");
        if (string.IsNullOrWhiteSpace(connection))
            throw new InvalidOperationException("Set ERRANDY_PAYMENT_TEST_CONNECTION for the dedicated local test database.");
        NpgsqlConnectionStringBuilder parsed;
        try { parsed = new NpgsqlConnectionStringBuilder(connection); }
        catch (ArgumentException) { throw new InvalidOperationException("The test connection string is invalid."); }
        if (!string.Equals(parsed.Host, "localhost", StringComparison.OrdinalIgnoreCase) || parsed.Port != 5432 ||
            !string.Equals(parsed.Database, "errandy_payment_tests", StringComparison.Ordinal))
            throw new InvalidOperationException("Tests are restricted to localhost:5432/errandy_payment_tests.");
        return connection;
    }
}
