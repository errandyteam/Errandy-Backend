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

public class PaymentReconciliationAuditPostgresTests : IAsyncLifetime
{
    private readonly string _connection = GetSafeTestConnection();
    private readonly Guid _adminId = Guid.NewGuid();
    private readonly Guid _customerId = Guid.NewGuid();
    private readonly Guid _walletId = Guid.NewGuid();
    private readonly string _reference = $"PAY-{Guid.NewGuid():N}";
    private Guid _intentId;

    public async Task InitializeAsync()
    {
        await using var db = CreateContext();
        var phone = RandomNumberGenerator.GetInt32(10_000_000, 99_999_998);
        db.Users.Add(new User
        {
            Id = _adminId,
            FullName = "Audit Test Admin",
            Email = $"{_adminId:N}@tests.invalid",
            PhoneNumber = "080" + phone.ToString(),
            PasswordHash = "test-only-not-a-real-password",
            Role = UserRole.Admin
        });
        db.Users.Add(new User
        {
            Id = _customerId,
            FullName = "Audit Test Customer",
            Email = $"{_customerId:N}@tests.invalid",
            PhoneNumber = "080" + (phone + 1).ToString(),
            PasswordHash = "test-only-not-a-real-password",
            Role = UserRole.Customer
        });
        db.Wallets.Add(new Wallet
        {
            Id = _walletId,
            UserId = _customerId,
            Balance = 100m,
            LockedBalance = 0m,
            Currency = "NGN",
            IsActive = true
        });
        var intent = PaymentIntent.Create(_customerId, _walletId, 50m, _reference,
            $"{_customerId:N}@tests.invalid", "Paystack");
        _intentId = intent.Id;
        db.PaymentIntents.Add(intent);
        await db.SaveChangesAsync();
    }

    public async Task DisposeAsync()
    {
        // Remove only rows created for this test instance, in foreign-key order.
        await using var db = CreateContext();
        var attempts = await db.PaymentReconciliationAttempts
            .Where(a => a.PaymentIntentId == _intentId && a.AdminUserId == _adminId).ToListAsync();
        db.PaymentReconciliationAttempts.RemoveRange(attempts);
        await db.SaveChangesAsync();
        var intent = await db.PaymentIntents.SingleOrDefaultAsync(p => p.Id == _intentId && p.UserId == _customerId);
        if (intent is not null) { db.PaymentIntents.Remove(intent); await db.SaveChangesAsync(); }
        var transactions = await db.Transactions.Where(t => t.WalletId == _walletId).ToListAsync();
        db.Transactions.RemoveRange(transactions);
        await db.SaveChangesAsync();
        var wallet = await db.Wallets.SingleOrDefaultAsync(w => w.Id == _walletId && w.UserId == _customerId);
        if (wallet is not null) { db.Wallets.Remove(wallet); await db.SaveChangesAsync(); }
        var users = await db.Users.Where(u => u.Id == _adminId || u.Id == _customerId).ToListAsync();
        db.Users.RemoveRange(users);
        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task Audit_CanBeStartedAndFinalized_WithoutChangingWalletOrIntent()
    {
        var requestedAt = new DateTime(2026, 1, 2, 3, 4, 5, DateTimeKind.Utc);
        var attempt = PaymentReconciliationAttempt.Start(_intentId, _adminId, _reference, "Review requested by customer", requestedAt);
        await using (var db = CreateContext())
        {
            db.PaymentReconciliationAttempts.Add(attempt);
            await db.SaveChangesAsync();
        }
        await using (var db = CreateContext())
        {
            var stored = await db.PaymentReconciliationAttempts.SingleAsync(a => a.Id == attempt.Id);
            Assert.Equal(PaymentReconciliationOutcome.Started, stored.Outcome);
            Assert.Null(stored.FinishedAt);
            stored.Finish(PaymentReconciliationOutcome.NotCompleted, "VerificationNotCompleted", requestedAt.AddMinutes(1));
            await db.SaveChangesAsync();
        }
        await using (var db = CreateContext())
        {
            var stored = await db.PaymentReconciliationAttempts.AsNoTracking().SingleAsync(a => a.Id == attempt.Id);
            Assert.Equal(PaymentReconciliationOutcome.NotCompleted, stored.Outcome);
            Assert.Equal("VerificationNotCompleted", stored.ResultCode);
            Assert.Equal((DateTime?)requestedAt.AddMinutes(1), stored.FinishedAt);
            Assert.Equal(PaymentStatus.Pending, (await db.PaymentIntents.AsNoTracking().SingleAsync(p => p.Id == _intentId)).Status);
            Assert.Equal(100m, (await db.Wallets.AsNoTracking().SingleAsync(w => w.Id == _walletId)).Balance);
            Assert.False(await db.Transactions.AsNoTracking().AnyAsync(t => t.WalletId == _walletId));
        }
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ExistingAudit_PreventsDeletingTheReferencedPaymentOrAdmin(bool deletePayment)
    {
        await using (var db = CreateContext())
        {
            db.PaymentReconciliationAttempts.Add(PaymentReconciliationAttempt.Start(
                _intentId, _adminId, _reference, "Audit retention test", DateTime.UtcNow));
            await db.SaveChangesAsync();
        }

        await using (var db = CreateContext())
        {
            if (deletePayment)
                db.PaymentIntents.Remove(await db.PaymentIntents.SingleAsync(p => p.Id == _intentId));
            else
                db.Users.Remove(await db.Users.SingleAsync(u => u.Id == _adminId));

            var error = await Assert.ThrowsAsync<DbUpdateException>(async () => await db.SaveChangesAsync());
            var postgres = Assert.IsType<PostgresException>(error.InnerException);
            // PostgreSQL 18 can report RESTRICT as 23001; older versions may use 23503.
            Assert.True(postgres.SqlState == "23001" || postgres.SqlState == PostgresErrorCodes.ForeignKeyViolation);
            Assert.Equal(deletePayment
                ? "FK_PaymentReconciliationAttempts_PaymentIntents_PaymentIntentId"
                : "FK_PaymentReconciliationAttempts_Users_AdminUserId", postgres.ConstraintName);
        }

        // Confirm the rejected delete left the intended records intact.
        await using var verify = CreateContext();
        Assert.True(await verify.PaymentIntents.AsNoTracking().AnyAsync(p => p.Id == _intentId));
        Assert.True(await verify.Users.AsNoTracking().AnyAsync(u => u.Id == _adminId));
        Assert.True(await verify.PaymentReconciliationAttempts.AsNoTracking()
            .AnyAsync(a => a.PaymentIntentId == _intentId && a.AdminUserId == _adminId));
    }

    private ErrandyDbContext CreateContext() => new(
        new DbContextOptionsBuilder<ErrandyDbContext>().UseNpgsql(_connection).Options);

    private static string GetSafeTestConnection()
    {
        var connection = Environment.GetEnvironmentVariable("ERRANDY_PAYMENT_TEST_CONNECTION");
        if (string.IsNullOrWhiteSpace(connection))
            throw new InvalidOperationException("Set ERRANDY_PAYMENT_TEST_CONNECTION for the dedicated local test database.");
        NpgsqlConnectionStringBuilder parsed;
        try { parsed = new NpgsqlConnectionStringBuilder(connection); }
        catch (ArgumentException) { throw new InvalidOperationException("The test connection string is invalid."); }
        if (!string.Equals(parsed.Host, "localhost", StringComparison.OrdinalIgnoreCase) ||
            parsed.Port != 5432 ||
            !string.Equals(parsed.Database, "errandy_payment_tests", StringComparison.Ordinal))
            throw new InvalidOperationException("Tests are restricted to localhost:5432/errandy_payment_tests.");
        return connection;
    }
}
