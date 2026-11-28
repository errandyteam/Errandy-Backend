using System;
using System.Linq;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using Errandy.Application.Common.Interfaces;
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

// Tests a single-check claim only. No worker, real API, or Paystack is started.
public class AutomaticPaymentCheckClaimerPostgresTests : IAsyncLifetime
{
    private readonly string _connection = GetSafeTestConnection();
    private readonly Guid _customerId = Guid.NewGuid();
    private readonly Guid _walletId = Guid.NewGuid();
    private readonly string _reference = $"PAY-{Guid.NewGuid():N}";
    private readonly DateTime _due = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
    private Guid _intentId;
    private Guid _checkId;

    public async Task InitializeAsync()
    {
        await using var db = CreateContext();
        var phone = RandomNumberGenerator.GetInt32(10_000_000, 100_000_000);
        db.Users.Add(new User
        {
            Id = _customerId, FullName = "Claim Test Customer", Email = $"{_customerId:N}@tests.invalid",
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
        var check = AutomaticPaymentCheck.Schedule(_intentId, _due, 10);
        _checkId = check.Id;
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
    public async Task DueCheck_IsCommittedBeforeLeaseIsReturned()
    {
        using var provider = CreateServices();
        var result = await Claimer(provider, _due).TryClaimAsync(_checkId);
        Assert.Equal(AutomaticPaymentClaimStatus.Claimed, result.Status);
        Assert.NotNull(result.Lease);
        var lease = result.Lease!;
        Assert.Equal(_checkId, lease.CheckId);
        Assert.Equal(_intentId, lease.PaymentIntentId);
        Assert.NotEqual(Guid.Empty, lease.Token);
        Assert.Equal(_due.AddMinutes(2), lease.ExpiresAtUtc);
        Assert.Equal(1, lease.AttemptNumber);
        Assert.Equal(10, lease.MaximumAttempts);
        await using var verify = CreateContext();
        var stored = await verify.AutomaticPaymentChecks.AsNoTracking().SingleAsync(c => c.Id == _checkId);
        Assert.Equal(AutomaticPaymentCheckStatus.Leased, stored.Status);
        Assert.Equal((Guid?)lease.Token, stored.LeaseToken);
        Assert.Equal(1L, stored.Revision);
        Assert.Equal(PaymentStatus.Pending, (await verify.PaymentIntents.AsNoTracking().SingleAsync(p => p.Id == _intentId)).Status);
        Assert.Equal(100m, (await verify.Wallets.AsNoTracking().SingleAsync(w => w.Id == _walletId)).Balance);
        Assert.False(await verify.Transactions.AsNoTracking().AnyAsync(t => t.WalletId == _walletId));
    }

    [Fact]
    public async Task EarlyCheck_IsNotClaimed()
    {
        using var provider = CreateServices();
        var result = await Claimer(provider, _due.AddTicks(-1)).TryClaimAsync(_checkId);
        Assert.Equal(AutomaticPaymentClaimStatus.NotAvailable, result.Status);
        Assert.Null(result.Lease);
        await using var db = CreateContext();
        var stored = await db.AutomaticPaymentChecks.AsNoTracking().SingleAsync(c => c.Id == _checkId);
        Assert.Equal(AutomaticPaymentCheckStatus.Scheduled, stored.Status);
        Assert.Equal(0, stored.AttemptsStarted);
        Assert.Equal(0L, stored.Revision);
    }

    [Fact]
    public async Task MissingCheck_IsNotAvailable()
    {
        using var provider = CreateServices();
        var result = await Claimer(provider, _due).TryClaimAsync(Guid.NewGuid());
        Assert.Equal(AutomaticPaymentClaimStatus.NotAvailable, result.Status);
        Assert.Null(result.Lease);
    }

    [Fact]
    public async Task AlreadyLeasedCheck_IsNotClaimedAgain()
    {
        using var provider = CreateServices();
        var claimer = Claimer(provider, _due);
        var first = await claimer.TryClaimAsync(_checkId);
        var second = await claimer.TryClaimAsync(_checkId);
        Assert.Equal(AutomaticPaymentClaimStatus.Claimed, first.Status);
        Assert.Equal(AutomaticPaymentClaimStatus.NotAvailable, second.Status);
        Assert.Null(second.Lease);
        await using var verify = CreateContext();
        var stored = await verify.AutomaticPaymentChecks.AsNoTracking().SingleAsync(c => c.Id == _checkId);
        Assert.Equal(1, stored.AttemptsStarted);
        Assert.Equal((Guid?)first.Lease!.Token, stored.LeaseToken);
    }

    [Fact]
    public async Task ExpiredLease_IsNotAutomaticallyStolen()
    {
        using var provider = CreateServices();
        var first = await Claimer(provider, _due).TryClaimAsync(_checkId);
        var second = await Claimer(provider, _due.AddHours(1)).TryClaimAsync(_checkId);
        Assert.Equal(AutomaticPaymentClaimStatus.Claimed, first.Status);
        Assert.Equal(AutomaticPaymentClaimStatus.NotAvailable, second.Status);
        Assert.Null(second.Lease);
        // Explicit recovery is a separate operation and is not implemented here.
    }

    [Fact]
    public async Task TwoClaimantsAfterSameRead_OnlyOneGetsCommittedLease()
    {
        var barrier = new SaveBarrier();
        using var provider = CreateServices(barrier);
        var first = Claimer(provider, _due);
        var second = Claimer(provider, _due);

        // Both contexts reach SaveChanges only after reading and modifying the
        // same revision. The barrier forces an actual conflicting update.
        var results = await Task.WhenAll(first.TryClaimAsync(_checkId), second.TryClaimAsync(_checkId));
        var winner = Assert.Single(results, r => r.Status == AutomaticPaymentClaimStatus.Claimed);
        var loser = Assert.Single(results, r => r.Status == AutomaticPaymentClaimStatus.Contended);
        Assert.NotNull(winner.Lease);
        Assert.Null(loser.Lease);
        await using var verify = CreateContext();
        var stored = await verify.AutomaticPaymentChecks.AsNoTracking().SingleAsync(c => c.Id == _checkId);
        Assert.Equal((Guid?)winner.Lease!.Token, stored.LeaseToken);
        Assert.Equal(1, stored.AttemptsStarted);
        Assert.Equal(1L, stored.Revision);
    }

    [Fact]
    public async Task Claim_IsCommittedOutsideCallerAmbientTransaction()
    {
        using var provider = CreateServices();
        AutomaticPaymentClaimResult result;
        using (var outer = new System.Transactions.TransactionScope(
            System.Transactions.TransactionScopeOption.Required,
            System.Transactions.TransactionScopeAsyncFlowOption.Enabled))
        {
            result = await Claimer(provider, _due).TryClaimAsync(_checkId);
            Assert.Equal(AutomaticPaymentClaimStatus.Claimed, result.Status);
            // The caller does not complete its transaction.
        }
        await using var verify = CreateContext();
        var stored = await verify.AutomaticPaymentChecks.AsNoTracking().SingleAsync(c => c.Id == _checkId);
        Assert.Equal(AutomaticPaymentCheckStatus.Leased, stored.Status);
        Assert.Equal((Guid?)result.Lease!.Token, stored.LeaseToken);
    }

    [Fact]
    public async Task LeaseExpiringBeforeReturn_IsNotHandedToCaller()
    {
        using var provider = CreateServices();
        var clock = new SequenceClock(_due, _due.AddMinutes(2));
        var claimer = new AutomaticPaymentCheckClaimer(provider.GetRequiredService<IServiceScopeFactory>(), clock, TimeSpan.FromMinutes(2));
        var result = await claimer.TryClaimAsync(_checkId);
        Assert.Equal(AutomaticPaymentClaimStatus.LeaseUnusableAfterCommit, result.Status);
        Assert.Null(result.Lease);
        await using var verify = CreateContext();
        var stored = await verify.AutomaticPaymentChecks.AsNoTracking().SingleAsync(c => c.Id == _checkId);
        Assert.Equal(AutomaticPaymentCheckStatus.Leased, stored.Status);
        Assert.Equal(1, stored.AttemptsStarted);
        // The expired lease needs explicit recovery; no verifier was started.
    }

    [Fact]
    public async Task InvalidArguments_AreRejected()
    {
        using var provider = CreateServices();
        var clock = new FixedClock(_due);
        var factory = provider.GetRequiredService<IServiceScopeFactory>();
        Assert.Throws<ArgumentOutOfRangeException>(() => new AutomaticPaymentCheckClaimer(factory, clock, TimeSpan.Zero));
        Assert.Throws<ArgumentOutOfRangeException>(() => new AutomaticPaymentCheckClaimer(factory, clock, TimeSpan.FromHours(25)));
        await Assert.ThrowsAsync<ArgumentException>(() => Claimer(provider, _due).TryClaimAsync(Guid.Empty));
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

    private static AutomaticPaymentCheckClaimer Claimer(ServiceProvider provider, DateTime now) =>
        new(provider.GetRequiredService<IServiceScopeFactory>(), new FixedClock(now), TimeSpan.FromMinutes(2));
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

    private sealed class SequenceClock : IDateTimeProvider
    {
        private readonly DateTime _first;
        private readonly DateTime _second;
        private int _reads;
        public SequenceClock(DateTime first, DateTime second) { _first = first; _second = second; }
        public DateTime UtcNow => Interlocked.Increment(ref _reads) == 1 ? _first : _second;
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
