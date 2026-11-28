using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using Errandy.Domain.Entities;
using Errandy.Domain.Enums;
using Errandy.Infrastructure.Payments;
using Errandy.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Xunit;

namespace Errandy.Tests.Payments;

// Tests isolate their queries to their own fixture IDs. Discovery is read-only;
// no hosted worker, API, gateway, or global backfill is started.
public class AutomaticPaymentCheckDiscoveryPostgresTests : IAsyncLifetime
{
    private readonly string _connection = GetSafeTestConnection();
    private readonly Guid _customerId = Guid.NewGuid();
    private readonly Guid _walletId = Guid.NewGuid();
    private readonly DateTime _now = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
    private readonly List<Guid> _intentIds = new();
    private Guid _firstUnscheduled;
    private Guid _secondUnscheduled;
    private Guid _dueOne;
    private Guid _dueTwo;
    private Guid _future;
    private Guid _expiredEarlier;
    private Guid _expiredNow;
    private Guid _liveLease;
    private Guid _needsReview;

    public async Task InitializeAsync()
    {
        await using var db = CreateContext();
        var phone = RandomNumberGenerator.GetInt32(10_000_000, 100_000_000);
        db.Users.Add(new User
        {
            Id = _customerId,
            FullName = "Discovery Test Customer",
            Email = $"{_customerId:N}@tests.invalid",
            PhoneNumber = "080" + phone.ToString(),
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

        var first = NewIntent("Paystack", _now.AddDays(-10));
        var second = NewIntent("paystack", _now.AddDays(-9));
        _firstUnscheduled = first.Id;
        _secondUnscheduled = second.Id;
        var dueOne = NewIntent("Paystack", _now.AddDays(-8));
        var dueTwo = NewIntent("Paystack", _now.AddDays(-7));
        var future = NewIntent("Paystack", _now.AddDays(-6));
        var expiredEarlier = NewIntent("Paystack", _now.AddDays(-5));
        var expiredNow = NewIntent("Paystack", _now.AddDays(-4));
        var liveLease = NewIntent("Paystack", _now.AddDays(-3));
        var needsReview = NewIntent("Paystack", _now.AddDays(-2));
        var otherProvider = NewIntent("OtherProvider", _now.AddDays(-20));
        var succeeded = NewIntent("Paystack", _now.AddDays(-21));
        // Synthetic status only: the discovery query doesn't inspect the ledger.
        succeeded.MarkSucceeded(Guid.NewGuid(), "test", "test", _now, _now);
        var failed = NewIntent("Paystack", _now.AddDays(-22));
        failed.MarkFailed("test", _now);
        db.PaymentIntents.AddRange(first, second, dueOne, dueTwo, future, expiredEarlier, expiredNow,
            liveLease, needsReview, otherProvider, succeeded, failed);
        await db.SaveChangesAsync();

        var dueCheckOne = AutomaticPaymentCheck.Schedule(dueOne.Id, _now.AddMinutes(-1), 2);
        var dueCheckTwo = AutomaticPaymentCheck.Schedule(dueTwo.Id, _now.AddMinutes(-1), 2);
        var futureCheck = AutomaticPaymentCheck.Schedule(future.Id, _now.AddMinutes(1), 2);
        var expiredCheckEarlier = AutomaticPaymentCheck.Schedule(expiredEarlier.Id, _now.AddMinutes(-3), 2);
        Assert.True(expiredCheckEarlier.TryStart(Guid.NewGuid(), _now.AddMinutes(-3), TimeSpan.FromMinutes(2)));
        var expiredCheckNow = AutomaticPaymentCheck.Schedule(expiredNow.Id, _now.AddMinutes(-2), 2);
        Assert.True(expiredCheckNow.TryStart(Guid.NewGuid(), _now.AddMinutes(-2), TimeSpan.FromMinutes(2)));
        var liveCheck = AutomaticPaymentCheck.Schedule(liveLease.Id, _now.AddMinutes(-1), 2);
        Assert.True(liveCheck.TryStart(Guid.NewGuid(), _now.AddMinutes(-1), TimeSpan.FromMinutes(2)));
        var reviewCheck = AutomaticPaymentCheck.Schedule(needsReview.Id, _now.AddMinutes(-4), 1);
        var reviewToken = Guid.NewGuid();
        Assert.True(reviewCheck.TryStart(reviewToken, _now.AddMinutes(-4), TimeSpan.FromMinutes(2)));
        reviewCheck.MarkUnconfirmed(reviewToken, _now.AddMinutes(-3), null);
        _dueOne = dueCheckOne.Id;
        _dueTwo = dueCheckTwo.Id;
        _future = futureCheck.Id;
        _expiredEarlier = expiredCheckEarlier.Id;
        _expiredNow = expiredCheckNow.Id;
        _liveLease = liveCheck.Id;
        _needsReview = reviewCheck.Id;
        db.AutomaticPaymentChecks.AddRange(dueCheckOne, dueCheckTwo, futureCheck, expiredCheckEarlier,
            expiredCheckNow, liveCheck, reviewCheck);
        await db.SaveChangesAsync();
    }

    public async Task DisposeAsync()
    {
        await using var db = CreateContext();
        var checks = await db.AutomaticPaymentChecks.Where(c => _intentIds.Contains(c.PaymentIntentId)).ToListAsync();
        db.AutomaticPaymentChecks.RemoveRange(checks);
        await db.SaveChangesAsync();
        var intents = await db.PaymentIntents.Where(p => p.UserId == _customerId && p.WalletId == _walletId).ToListAsync();
        db.PaymentIntents.RemoveRange(intents);
        await db.SaveChangesAsync();
        var wallet = await db.Wallets.SingleOrDefaultAsync(w => w.Id == _walletId && w.UserId == _customerId);
        if (wallet is not null) { db.Wallets.Remove(wallet); await db.SaveChangesAsync(); }
        var user = await db.Users.SingleOrDefaultAsync(u => u.Id == _customerId);
        if (user is not null) { db.Users.Remove(user); await db.SaveChangesAsync(); }
    }

    [Fact]
    public async Task Unscheduled_FiltersAndOrdersOldestFirst_WithBoundedBatch()
    {
        await using var db = CreateContext();
        var payments = Payments(db);
        var checks = Checks(db);
        var all = await AutomaticPaymentCheckDiscovery.FindUnscheduledPaymentsAsync(payments, checks, 100);
        var one = await AutomaticPaymentCheckDiscovery.FindUnscheduledPaymentsAsync(payments, checks, 1);
        Assert.Equal(new[] { _firstUnscheduled, _secondUnscheduled }, all.ToArray());
        Assert.Equal(new[] { _firstUnscheduled }, one.ToArray());
    }

    [Fact]
    public async Task Due_ExcludesFutureLeasedAndReview_AndUsesStableTieOrdering()
    {
        await using var db = CreateContext();
        var all = await AutomaticPaymentCheckDiscovery.FindDueChecksAsync(Checks(db), _now, 100);
        var one = await AutomaticPaymentCheckDiscovery.FindDueChecksAsync(Checks(db), _now, 1);
        var again = await AutomaticPaymentCheckDiscovery.FindDueChecksAsync(Checks(db), _now, 100);
        Assert.Equal(2, all.Count);
        Assert.Contains(_dueOne, all);
        Assert.Contains(_dueTwo, all);
        Assert.DoesNotContain(_future, all);
        Assert.DoesNotContain(_liveLease, all);
        Assert.DoesNotContain(_needsReview, all);
        Assert.Equal(all.ToArray(), again.ToArray());
        Assert.Equal(all[0], Assert.Single(one));
    }

    [Fact]
    public async Task Expired_IncludesExpiryBoundaryAndOrdersOldestFirst()
    {
        await using var db = CreateContext();
        var all = await AutomaticPaymentCheckDiscovery.FindExpiredLeasesAsync(Checks(db), _now, 100);
        var one = await AutomaticPaymentCheckDiscovery.FindExpiredLeasesAsync(Checks(db), _now, 1);
        Assert.Equal(new[] { _expiredEarlier, _expiredNow }, all.ToArray());
        Assert.Equal(new[] { _expiredEarlier }, one.ToArray());
        Assert.DoesNotContain(_liveLease, all);
    }

    [Fact]
    public async Task Discovery_DoesNotModifyChecksPaymentsOrWallet()
    {
        await using (var db = CreateContext())
        {
            await AutomaticPaymentCheckDiscovery.FindUnscheduledPaymentsAsync(Payments(db), Checks(db), 100);
            await AutomaticPaymentCheckDiscovery.FindDueChecksAsync(Checks(db), _now, 100);
            await AutomaticPaymentCheckDiscovery.FindExpiredLeasesAsync(Checks(db), _now, 100);
        }
        await using var verify = CreateContext();
        Assert.Equal(7, await Checks(verify).CountAsync());
        Assert.Equal(AutomaticPaymentCheckStatus.Leased,
            (await verify.AutomaticPaymentChecks.AsNoTracking().SingleAsync(c => c.Id == _expiredEarlier)).Status);
        Assert.Equal(AutomaticPaymentCheckStatus.NeedsReview,
            (await verify.AutomaticPaymentChecks.AsNoTracking().SingleAsync(c => c.Id == _needsReview)).Status);
        Assert.Equal(100m, (await verify.Wallets.AsNoTracking().SingleAsync(w => w.Id == _walletId)).Balance);
        Assert.False(await verify.Transactions.AsNoTracking().AnyAsync(t => t.WalletId == _walletId));
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(0)]
    [InlineData(101)]
    public async Task InvalidBatchSize_IsRejectedByAllQueries(int batchSize)
    {
        await using var db = CreateContext();
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => AutomaticPaymentCheckDiscovery.FindUnscheduledPaymentsAsync(Payments(db), Checks(db), batchSize));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => AutomaticPaymentCheckDiscovery.FindDueChecksAsync(Checks(db), _now, batchSize));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => AutomaticPaymentCheckDiscovery.FindExpiredLeasesAsync(Checks(db), _now, batchSize));
    }

    [Fact]
    public async Task NonUtcTime_IsRejected()
    {
        await using var db = CreateContext();
        var nonUtc = DateTime.SpecifyKind(_now, DateTimeKind.Unspecified);
        await Assert.ThrowsAsync<ArgumentException>(() => AutomaticPaymentCheckDiscovery.FindDueChecksAsync(Checks(db), nonUtc, 10));
        await Assert.ThrowsAsync<ArgumentException>(() => AutomaticPaymentCheckDiscovery.FindExpiredLeasesAsync(Checks(db), nonUtc, 10));
    }

    [Fact]
    public async Task CancelledQuery_PropagatesCancellation()
    {
        await using var db = CreateContext();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            AutomaticPaymentCheckDiscovery.FindDueChecksAsync(Checks(db), _now, 10, cancellation.Token));
    }

    [Fact]
    public async Task UnscheduledCursorPages_ReturnAllRowsWithoutDuplicates()
    {
        await using var db = CreateContext();
        var first = await AutomaticPaymentCheckDiscoveryPages.FindUnscheduledPaymentsAsync(Payments(db), Checks(db), 1);
        var second = await AutomaticPaymentCheckDiscoveryPages.FindUnscheduledPaymentsAsync(Payments(db), Checks(db), 1, Assert.Single(first).ToCursor());
        var end = await AutomaticPaymentCheckDiscoveryPages.FindUnscheduledPaymentsAsync(Payments(db), Checks(db), 1, Assert.Single(second).ToCursor());
        Assert.Equal(_firstUnscheduled, first[0].Id);
        Assert.Equal(_secondUnscheduled, second[0].Id);
        Assert.Empty(end);
    }

    [Fact]
    public async Task DueCursorPages_HandleEqualTimestampsInDatabaseOrder()
    {
        await using var db = CreateContext();
        var expected = await AutomaticPaymentCheckDiscovery.FindDueChecksAsync(Checks(db), _now, 100);
        var first = await AutomaticPaymentCheckDiscoveryPages.FindDueChecksAsync(Checks(db), _now, 1);
        var second = await AutomaticPaymentCheckDiscoveryPages.FindDueChecksAsync(Checks(db), _now, 1, Assert.Single(first).ToCursor());
        var end = await AutomaticPaymentCheckDiscoveryPages.FindDueChecksAsync(Checks(db), _now, 1, Assert.Single(second).ToCursor());
        Assert.Equal(first[0].OrderAtUtc, second[0].OrderAtUtc);
        Assert.Equal(expected.ToArray(), new[] { first[0].Id, second[0].Id });
        Assert.NotEqual(first[0].Id, second[0].Id);
        Assert.Empty(end);
    }

    [Fact]
    public async Task ExpiredCursorPages_ReturnAllRowsInExpiryOrder()
    {
        await using var db = CreateContext();
        var first = await AutomaticPaymentCheckDiscoveryPages.FindExpiredLeasesAsync(Checks(db), _now, 1);
        var second = await AutomaticPaymentCheckDiscoveryPages.FindExpiredLeasesAsync(Checks(db), _now, 1, Assert.Single(first).ToCursor());
        var end = await AutomaticPaymentCheckDiscoveryPages.FindExpiredLeasesAsync(Checks(db), _now, 1, Assert.Single(second).ToCursor());
        Assert.Equal(_expiredEarlier, first[0].Id);
        Assert.Equal(_expiredNow, second[0].Id);
        Assert.Empty(end);
    }

    [Fact]
    public async Task DueCursor_CanAdvanceWhenFirstRowLeavesTheQuery()
    {
        await using var db = CreateContext();
        var first = await AutomaticPaymentCheckDiscoveryPages.FindDueChecksAsync(Checks(db), _now, 1);
        var item = Assert.Single(first);
        var check = await db.AutomaticPaymentChecks.SingleAsync(c => c.Id == item.Id);
        Assert.True(check.TryStart(Guid.NewGuid(), _now, TimeSpan.FromMinutes(2)));
        await db.SaveChangesAsync();
        var second = await AutomaticPaymentCheckDiscoveryPages.FindDueChecksAsync(Checks(db), _now, 1, item.ToCursor());
        Assert.NotEqual(item.Id, Assert.Single(second).Id);
        Assert.Contains(second[0].Id, new[] { _dueOne, _dueTwo });
    }

    [Fact]
    public async Task InvalidCursor_IsRejectedByAllPages()
    {
        await using var db = CreateContext();
        var badId = new AutomaticPaymentDiscoveryCursor(_now, Guid.Empty);
        var badTime = new AutomaticPaymentDiscoveryCursor(DateTime.SpecifyKind(_now, DateTimeKind.Unspecified), Guid.NewGuid());
        await Assert.ThrowsAsync<ArgumentException>(() => AutomaticPaymentCheckDiscoveryPages.FindUnscheduledPaymentsAsync(Payments(db), Checks(db), 10, badId));
        await Assert.ThrowsAsync<ArgumentException>(() => AutomaticPaymentCheckDiscoveryPages.FindDueChecksAsync(Checks(db), _now, 10, badTime));
        await Assert.ThrowsAsync<ArgumentException>(() => AutomaticPaymentCheckDiscoveryPages.FindExpiredLeasesAsync(Checks(db), _now, 10, badId));
        await Assert.ThrowsAsync<ArgumentException>(() => AutomaticPaymentCheckDiscoveryPages.FindExpiredLeasesAsync(Checks(db), badTime.OrderAtUtc, 10));
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(0)]
    [InlineData(101)]
    public async Task InvalidPageSize_IsRejectedByAllPages(int size)
    {
        await using var db = CreateContext();
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => AutomaticPaymentCheckDiscoveryPages.FindUnscheduledPaymentsAsync(Payments(db), Checks(db), size));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => AutomaticPaymentCheckDiscoveryPages.FindDueChecksAsync(Checks(db), _now, size));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => AutomaticPaymentCheckDiscoveryPages.FindExpiredLeasesAsync(Checks(db), _now, size));
    }

    private PaymentIntent NewIntent(string provider, DateTime createdAt)
    {
        var intent = PaymentIntent.Create(_customerId, _walletId, 50m, $"PAY-{Guid.NewGuid():N}",
            $"{_customerId:N}@tests.invalid", provider);
        intent.CreatedAt = createdAt;
        _intentIds.Add(intent.Id);
        return intent;
    }

    private IQueryable<PaymentIntent> Payments(ErrandyDbContext db) => db.PaymentIntents.AsNoTracking().Where(p => p.UserId == _customerId);
    private IQueryable<AutomaticPaymentCheck> Checks(ErrandyDbContext db) => db.AutomaticPaymentChecks.AsNoTracking().Where(c => _intentIds.Contains(c.PaymentIntentId));
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
