using System;
using System.Linq;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using Errandy.Application.Common.Interfaces;
using Errandy.Application.Features.Payments.Reconciliation;
using Errandy.Application.Interfaces;
using Errandy.Domain.Entities;
using Errandy.Domain.Enums;
using Errandy.Infrastructure.Payments;
using Errandy.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Xunit;

namespace Errandy.Tests.Payments;

// These tests recover explicitly selected checks only. They do not start the
// application, a worker, or Paystack, or attempt to credit a wallet.
public class AutomaticPaymentCheckRecoveryPostgresTests : IAsyncLifetime
{
    private readonly string _connection = GetSafeTestConnection();
    private readonly Guid _customerId = Guid.NewGuid();
    private readonly Guid _walletId = Guid.NewGuid();
    private readonly string _reference = $"PAY-{Guid.NewGuid():N}";
    private readonly DateTime _due = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
    private Guid _intentId;
    private Guid _checkId;
    private Guid _oldToken;
    private DateTime Expiry => _due.AddMinutes(2);

    public async Task InitializeAsync()
    {
        await using var db = CreateContext();
        var phone = RandomNumberGenerator.GetInt32(10_000_000, 100_000_000);
        db.Users.Add(new User
        {
            Id = _customerId, FullName = "Recovery Test Customer", Email = $"{_customerId:N}@tests.invalid",
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
        var check = AutomaticPaymentCheck.Schedule(_intentId, _due, 2);
        _checkId = check.Id;
        _oldToken = Guid.NewGuid();
        Assert.True(check.TryStart(_oldToken, _due, TimeSpan.FromMinutes(2)));
        db.AutomaticPaymentChecks.Add(check);
        await db.SaveChangesAsync();
    }

    public async Task DisposeAsync()
    {
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
    public async Task ExpiredLease_IsRescheduledUsingExpiryAsBackoffAnchor()
    {
        using var provider = CreateServices();
        var result = await Recovery(provider, Expiry).RecoverAsync(_checkId);
        Assert.Equal(AutomaticPaymentRecoveryResult.Rescheduled, result);
        await using var verify = CreateContext();
        var check = await verify.AutomaticPaymentChecks.AsNoTracking().SingleAsync(c => c.Id == _checkId);
        Assert.Equal(AutomaticPaymentCheckStatus.Scheduled, check.Status);
        Assert.Equal(AutomaticPaymentCheckResult.LeaseExpired, check.LastResult);
        Assert.Equal((DateTime?)Expiry.AddMinutes(5), check.NextAttemptAt);
        Assert.Equal((DateTime?)Expiry, check.LastFinishedAt);
        Assert.Null(check.LeaseToken);
        Assert.Null(check.LeaseExpiresAt);
        Assert.Equal(1, check.AttemptsStarted);
        Assert.Equal(2L, check.Revision);
        Assert.Equal(PaymentStatus.Pending, (await verify.PaymentIntents.AsNoTracking().SingleAsync(p => p.Id == _intentId)).Status);
        Assert.Equal(100m, (await verify.Wallets.AsNoTracking().SingleAsync(w => w.Id == _walletId)).Balance);
        Assert.False(await verify.Transactions.AsNoTracking().AnyAsync(t => t.WalletId == _walletId));
    }

    [Fact]
    public async Task UnexpiredLease_IsNotRecovered()
    {
        using var provider = CreateServices();
        var result = await Recovery(provider, Expiry.AddTicks(-1)).RecoverAsync(_checkId);
        Assert.Equal(AutomaticPaymentRecoveryResult.NotRecoverable, result);
        await using var db = CreateContext();
        var check = await db.AutomaticPaymentChecks.AsNoTracking().SingleAsync(c => c.Id == _checkId);
        Assert.Equal(AutomaticPaymentCheckStatus.Leased, check.Status);
        Assert.Equal((Guid?)_oldToken, check.LeaseToken);
        Assert.Equal(1L, check.Revision);
    }

    [Fact]
    public async Task MissingOrAlreadyRecoveredCheck_IsNotRecoverable()
    {
        using var provider = CreateServices();
        var recovery = Recovery(provider, Expiry);
        Assert.Equal(AutomaticPaymentRecoveryResult.NotRecoverable, await recovery.RecoverAsync(Guid.NewGuid()));
        Assert.Equal(AutomaticPaymentRecoveryResult.Rescheduled, await recovery.RecoverAsync(_checkId));
        Assert.Equal(AutomaticPaymentRecoveryResult.NotRecoverable, await recovery.RecoverAsync(_checkId));
    }

    [Fact]
    public async Task LastExpiredAttempt_GoesToReviewWithoutDeclaringPaymentFailed()
    {
        var secondStart = _due.AddMinutes(6);
        var secondExpiry = secondStart.AddMinutes(2);
        await using (var db = CreateContext())
        {
            var check = await db.AutomaticPaymentChecks.SingleAsync(c => c.Id == _checkId);
            check.MarkUnconfirmed(_oldToken, _due.AddMinutes(1), secondStart);
            Assert.True(check.TryStart(Guid.NewGuid(), secondStart, TimeSpan.FromMinutes(2)));
            await db.SaveChangesAsync();
        }
        using var provider = CreateServices();
        Assert.Equal(AutomaticPaymentRecoveryResult.NeedsReview, await Recovery(provider, secondExpiry).RecoverAsync(_checkId));
        await using var verify = CreateContext();
        var stored = await verify.AutomaticPaymentChecks.AsNoTracking().SingleAsync(c => c.Id == _checkId);
        Assert.Equal(AutomaticPaymentCheckStatus.NeedsReview, stored.Status);
        Assert.Equal(AutomaticPaymentCheckResult.LeaseExpired, stored.LastResult);
        Assert.Equal(2, stored.AttemptsStarted);
        Assert.Null(stored.NextAttemptAt);
        Assert.Null(stored.LeaseToken);
        Assert.Equal(PaymentStatus.Pending, (await verify.PaymentIntents.AsNoTracking().SingleAsync(p => p.Id == _intentId)).Status);
    }

    [Fact]
    public async Task ChangedDefaultAttemptLimit_DoesNotOverridePersistedLimit()
    {
        using var provider = CreateServices();
        var policy = new AutomaticPaymentRetryPolicy(maximumAttempts: 1);
        Assert.Equal(AutomaticPaymentRecoveryResult.Rescheduled, await Recovery(provider, Expiry, policy).RecoverAsync(_checkId));
        await using var verify = CreateContext();
        var stored = await verify.AutomaticPaymentChecks.AsNoTracking().SingleAsync(c => c.Id == _checkId);
        Assert.Equal(2, stored.MaximumAttempts);
        Assert.Equal((DateTime?)Expiry.AddMinutes(5), stored.NextAttemptAt);
    }

    [Fact]
    public async Task LateRecovery_MayProduceAnAlreadyDueRetry()
    {
        using var provider = CreateServices();
        var now = Expiry.AddHours(1);
        Assert.Equal(AutomaticPaymentRecoveryResult.Rescheduled, await Recovery(provider, now).RecoverAsync(_checkId));
        await using var db = CreateContext();
        var check = await db.AutomaticPaymentChecks.AsNoTracking().SingleAsync(c => c.Id == _checkId);
        Assert.Equal((DateTime?)Expiry.AddMinutes(5), check.NextAttemptAt);
        Assert.True(check.NextAttemptAt < now);
    }

    [Fact]
    public async Task CompetingRecoveriesAfterSameRead_ExactlyOneCommits()
    {
        var barrier = new SaveBarrier();
        using var provider = CreateServices(barrier);
        var first = Recovery(provider, Expiry);
        var second = Recovery(provider, Expiry);
        var results = await Task.WhenAll(first.RecoverAsync(_checkId), second.RecoverAsync(_checkId));
        Assert.Equal(1, results.Count(r => r == AutomaticPaymentRecoveryResult.Rescheduled));
        Assert.Equal(1, results.Count(r => r == AutomaticPaymentRecoveryResult.Contended));
        await using var verify = CreateContext();
        var stored = await verify.AutomaticPaymentChecks.AsNoTracking().SingleAsync(c => c.Id == _checkId);
        Assert.Equal(AutomaticPaymentCheckStatus.Scheduled, stored.Status);
        Assert.Equal(2L, stored.Revision);
        Assert.Equal(1, stored.AttemptsStarted);
    }

    [Fact]
    public async Task RecoveryCommit_IsIndependentOfCallerAmbientTransaction()
    {
        using var provider = CreateServices();
        using (var outer = new System.Transactions.TransactionScope(
            System.Transactions.TransactionScopeOption.Required,
            System.Transactions.TransactionScopeAsyncFlowOption.Enabled))
        {
            Assert.Equal(AutomaticPaymentRecoveryResult.Rescheduled, await Recovery(provider, Expiry).RecoverAsync(_checkId));
            // Deliberately do not complete the caller's transaction.
        }
        await using var verify = CreateContext();
        var stored = await verify.AutomaticPaymentChecks.AsNoTracking().SingleAsync(c => c.Id == _checkId);
        Assert.Equal(AutomaticPaymentCheckStatus.Scheduled, stored.Status);
    }

    [Fact]
    public async Task InvalidIdAndNonUtcClock_AreRejected()
    {
        using var provider = CreateServices();
        await Assert.ThrowsAsync<ArgumentException>(() => Recovery(provider, Expiry).RecoverAsync(Guid.Empty));
        await Assert.ThrowsAsync<InvalidOperationException>(() => Recovery(provider, DateTime.SpecifyKind(Expiry, DateTimeKind.Unspecified)).RecoverAsync(_checkId));
        await using var verify = CreateContext();
        var stored = await verify.AutomaticPaymentChecks.AsNoTracking().SingleAsync(c => c.Id == _checkId);
        Assert.Equal(AutomaticPaymentCheckStatus.Leased, stored.Status);
        Assert.Equal((Guid?)_oldToken, stored.LeaseToken);
    }

    private ServiceProvider CreateServices(SaveBarrier? barrier = null)
    {
        var services = new ServiceCollection();
        if (barrier is null)
        {
            services.AddDbContext<ErrandyDbContext>(options => options.UseNpgsql(_connection));
            services.AddScoped<IApplicationDbContext>(sp => sp.GetRequiredService<ErrandyDbContext>());
        }
        else
        {
            var options = new DbContextOptionsBuilder<ErrandyDbContext>().UseNpgsql(_connection).Options;
            services.AddScoped<IApplicationDbContext>(_ => new BarrierContext(options, barrier));
        }
        return services.BuildServiceProvider(validateScopes: true);
    }

    private static AutomaticPaymentCheckRecovery Recovery(ServiceProvider provider, DateTime now, AutomaticPaymentRetryPolicy? policy = null) =>
        new(provider.GetRequiredService<IServiceScopeFactory>(), new FixedClock(now), policy ?? new AutomaticPaymentRetryPolicy());
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

    private sealed class FixedClock : IDateTimeProvider
    {
        public FixedClock(DateTime now) => UtcNow = now;
        public DateTime UtcNow { get; }
    }

    private sealed class SaveBarrier
    {
        private int _arrivals;
        private readonly TaskCompletionSource<bool> _bothArrived = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public async Task WaitAsync(CancellationToken cancellationToken)
        {
            if (Interlocked.Increment(ref _arrivals) == 2) _bothArrived.TrySetResult(true);
            await _bothArrived.Task.WaitAsync(TimeSpan.FromSeconds(15), cancellationToken);
        }
    }

    private sealed class BarrierContext : ErrandyDbContext
    {
        private readonly SaveBarrier _barrier;
        public BarrierContext(DbContextOptions<ErrandyDbContext> options, SaveBarrier barrier) : base(options) => _barrier = barrier;
        public override async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
        {
            await _barrier.WaitAsync(cancellationToken);
            return await base.SaveChangesAsync(cancellationToken);
        }
    }
}
