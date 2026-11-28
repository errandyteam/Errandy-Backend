using System;
using System.Linq;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using Errandy.Application.Common.DTOs;
using Errandy.Application.Common.Interfaces;
using Errandy.Application.Common.Results;
using Errandy.Application.Features.Payments.Queries.GetAutomaticPaymentReviewQueue;
using Errandy.Domain.Entities;
using Errandy.Domain.Enums;
using Errandy.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Xunit;

namespace Errandy.Tests.Payments;

// Handler-level PostgreSQL tests, with synthetic payment/check states. No API,
// automatic worker, real gateway, or Paystack calls are made.
public class AutomaticPaymentReviewQueuePostgresTests : IAsyncLifetime
{
    private readonly string _connection = GetSafeTestConnection();
    private readonly Guid _adminId = Guid.NewGuid();
    private readonly Guid _customerId = Guid.NewGuid();
    private readonly Guid _otherUserId = Guid.NewGuid();
    private readonly Guid _walletId = Guid.NewGuid();
    private readonly Guid _otherWalletId = Guid.NewGuid();
    private readonly DateTime _time = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
    private Guid _firstReviewId;
    private Guid _secondReviewId;
    private Guid _expiredReviewId;
    private Guid _scheduledId;
    private Guid _otherReviewId;

    public async Task InitializeAsync()
    {
        await using var db = CreateContext();
        var phone = RandomNumberGenerator.GetInt32(10_000_000, 99_999_997);
        db.Users.Add(new User
        {
            Id = _adminId, FullName = "Review Queue Admin", Email = $"{_adminId:N}@tests.invalid",
            PhoneNumber = "080" + phone.ToString(), PasswordHash = "test-only-not-a-real-password", Role = UserRole.Admin
        });
        db.Users.Add(new User
        {
            Id = _customerId, FullName = "Review Queue Customer", Email = $"{_customerId:N}@tests.invalid",
            PhoneNumber = "080" + (phone + 1).ToString(), PasswordHash = "test-only-not-a-real-password", Role = UserRole.Customer
        });
        db.Users.Add(new User
        {
            Id = _otherUserId, FullName = "Other Review Queue Customer", Email = $"{_otherUserId:N}@tests.invalid",
            PhoneNumber = "080" + (phone + 2).ToString(), PasswordHash = "test-only-not-a-real-password", Role = UserRole.Customer
        });
        db.Wallets.Add(new Wallet { Id = _walletId, UserId = _customerId, Balance = 100m, LockedBalance = 0m, Currency = "NGN", IsActive = true });
        db.Wallets.Add(new Wallet { Id = _otherWalletId, UserId = _otherUserId, Balance = 50m, LockedBalance = 0m, Currency = "NGN", IsActive = true });
        var pending = NewIntent(_customerId, _walletId);
        var succeeded = NewIntent(_customerId, _walletId);
        // Synthetic state to prove succeeded payments still appear for review.
        // The query does not assert a matching real funding ledger entry exists.
        succeeded.MarkSucceeded(Guid.NewGuid(), "test", "test", _time, _time);
        var expired = NewIntent(_customerId, _walletId);
        var scheduled = NewIntent(_customerId, _walletId);
        var other = NewIntent(_otherUserId, _otherWalletId);
        db.PaymentIntents.AddRange(pending, succeeded, expired, scheduled, other);
        await db.SaveChangesAsync();

        var first = NewReview(pending.Id);
        var second = NewReview(succeeded.Id);
        var expiry = AutomaticPaymentCheck.Schedule(expired.Id, _time, 1);
        Assert.True(expiry.TryStart(Guid.NewGuid(), _time, TimeSpan.FromMinutes(2)));
        expiry.RecoverExpiredLease(_time.AddMinutes(2), null);
        var notReview = AutomaticPaymentCheck.Schedule(scheduled.Id, _time, 2);
        var otherReview = NewReview(other.Id);
        _firstReviewId = first.Id;
        _secondReviewId = second.Id;
        _expiredReviewId = expiry.Id;
        _scheduledId = notReview.Id;
        _otherReviewId = otherReview.Id;
        db.AutomaticPaymentChecks.AddRange(first, second, expiry, notReview, otherReview);
        await db.SaveChangesAsync();
    }

    public async Task DisposeAsync()
    {
        await using var db = CreateContext();
        var ids = await db.PaymentIntents.Where(p => p.UserId == _customerId || p.UserId == _otherUserId).Select(p => p.Id).ToListAsync();
        var checks = await db.AutomaticPaymentChecks.Where(c => ids.Contains(c.PaymentIntentId)).ToListAsync();
        db.AutomaticPaymentChecks.RemoveRange(checks);
        await db.SaveChangesAsync();
        var intents = await db.PaymentIntents.Where(p => p.UserId == _customerId || p.UserId == _otherUserId).ToListAsync();
        db.PaymentIntents.RemoveRange(intents);
        await db.SaveChangesAsync();
        var wallets = await db.Wallets.Where(w => w.Id == _walletId || w.Id == _otherWalletId).ToListAsync();
        db.Wallets.RemoveRange(wallets);
        await db.SaveChangesAsync();
        var users = await db.Users.Where(u => u.Id == _adminId || u.Id == _customerId || u.Id == _otherUserId).ToListAsync();
        db.Users.RemoveRange(users);
        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task UnauthenticatedCaller_IsRejected()
    {
        await using var db = CreateContext();
        var result = await Handler(db, new FakeCurrentUser(null, false, null)).Handle(Query(), CancellationToken.None);
        Assert.False(result.IsSuccess);
        Assert.Equal(ErrorType.Unauthorized, result.ErrorType);
    }

    [Fact]
    public async Task CustomerWithAdminClaim_IsRejectedBeforeValidation()
    {
        await using var db = CreateContext();
        var result = await Handler(db, new FakeCurrentUser(_customerId, true, UserRole.Admin)).Handle(
            new GetAutomaticPaymentReviewQueueQuery(0, 0, Guid.Empty), CancellationToken.None);
        Assert.False(result.IsSuccess);
        Assert.Equal(ErrorType.Forbidden, result.ErrorType);
    }

    [Fact]
    public async Task MissingUserWithAdminClaim_IsRejected()
    {
        await using var db = CreateContext();
        var result = await Handler(db, new FakeCurrentUser(Guid.NewGuid(), true, UserRole.Admin)).Handle(Query(), CancellationToken.None);
        Assert.False(result.IsSuccess);
        Assert.Equal(ErrorType.Forbidden, result.ErrorType);
    }

    [Fact]
    public async Task DatabaseAdmin_SeesOnlyFilteredReviewCases_IncludingSucceededPayment()
    {
        await using var db = CreateContext();
        // Current database role is authoritative even when the claim differs.
        var result = await Handler(db, new FakeCurrentUser(_adminId, true, UserRole.Customer)).Handle(Query(), CancellationToken.None);
        Assert.True(result.IsSuccess, result.Error);
        Assert.NotNull(result.Value);
        var items = result.Value!.Items;
        Assert.Equal(3, items.Count);
        Assert.All(items, item => Assert.Equal(_customerId, item.UserId));
        Assert.Contains(items, item => item.CheckId == _firstReviewId && item.PaymentStatus == PaymentStatus.Pending && item.LastResult == AutomaticPaymentCheckResult.Unconfirmed);
        Assert.Contains(items, item => item.CheckId == _secondReviewId && item.PaymentStatus == PaymentStatus.Succeeded && item.PaymentStatusName == "Succeeded");
        Assert.Contains(items, item => item.CheckId == _expiredReviewId && item.LastResultName == "LeaseExpired");
        Assert.DoesNotContain(items, item => item.CheckId == _scheduledId || item.CheckId == _otherReviewId);
        Assert.All(items, item => { Assert.Equal(1, item.AttemptsStarted); Assert.Equal(1, item.MaximumAttempts); });
        Assert.False(result.Value.HasMore);
    }

    [Fact]
    public async Task Pagination_UsesStableOrderingForEqualFinishedTimes()
    {
        await using var db = CreateContext();
        var handler = Handler(db, Admin());
        var all = await handler.Handle(Query(), CancellationToken.None);
        var first = await handler.Handle(new GetAutomaticPaymentReviewQueueQuery(1, 1, _customerId), CancellationToken.None);
        var second = await handler.Handle(new GetAutomaticPaymentReviewQueueQuery(2, 1, _customerId), CancellationToken.None);
        var third = await handler.Handle(new GetAutomaticPaymentReviewQueueQuery(3, 1, _customerId), CancellationToken.None);
        Assert.True(all.IsSuccess, all.Error); Assert.True(first.IsSuccess, first.Error);
        Assert.True(second.IsSuccess, second.Error); Assert.True(third.IsSuccess, third.Error);
        Assert.NotNull(all.Value); Assert.NotNull(first.Value); Assert.NotNull(second.Value); Assert.NotNull(third.Value);
        var pages = new[] { Assert.Single(first.Value!.Items), Assert.Single(second.Value!.Items), Assert.Single(third.Value!.Items) };
        Assert.Equal(all.Value!.Items.Select(i => i.CheckId).ToArray(), pages.Select(i => i.CheckId).ToArray());
        Assert.Equal(pages[0].LastFinishedAt, pages[1].LastFinishedAt);
        Assert.Equal(_expiredReviewId, pages[2].CheckId);
        Assert.True(first.Value.HasMore); Assert.True(second.Value.HasMore); Assert.False(third.Value.HasMore);
    }

    [Fact]
    public async Task UnfilteredQueue_IsValidForAdmin()
    {
        await using var db = CreateContext();
        var result = await Handler(db, Admin()).Handle(new GetAutomaticPaymentReviewQueueQuery(), CancellationToken.None);
        Assert.True(result.IsSuccess, result.Error);
        Assert.NotNull(result.Value);
        Assert.Equal(1, result.Value!.Page);
        Assert.Equal(50, result.Value.PageSize);
    }

    [Fact]
    public async Task UnknownUserFilter_ReturnsEmptyPage()
    {
        await using var db = CreateContext();
        var result = await Handler(db, Admin()).Handle(new GetAutomaticPaymentReviewQueueQuery(1, 50, Guid.NewGuid()), CancellationToken.None);
        Assert.True(result.IsSuccess, result.Error);
        Assert.NotNull(result.Value);
        Assert.Empty(result.Value!.Items);
        Assert.False(result.Value.HasMore);
    }

    [Theory]
    [InlineData(0, 10)]
    [InlineData(10001, 10)]
    [InlineData(1, 0)]
    [InlineData(1, 101)]
    public async Task InvalidPagination_IsRejected(int page, int pageSize)
    {
        await using var db = CreateContext();
        var result = await Handler(db, Admin()).Handle(new GetAutomaticPaymentReviewQueueQuery(page, pageSize, _customerId), CancellationToken.None);
        Assert.False(result.IsSuccess);
        Assert.Equal(ErrorType.Validation, result.ErrorType);
    }

    [Fact]
    public async Task EmptyUserFilter_IsRejected()
    {
        await using var db = CreateContext();
        var result = await Handler(db, Admin()).Handle(new GetAutomaticPaymentReviewQueueQuery(1, 50, Guid.Empty), CancellationToken.None);
        Assert.False(result.IsSuccess);
        Assert.Equal(ErrorType.Validation, result.ErrorType);
    }

    [Fact]
    public async Task ReadingQueue_DoesNotModifyChecksOrWallets()
    {
        await using (var db = CreateContext())
        {
            var result = await Handler(db, Admin()).Handle(Query(), CancellationToken.None);
            Assert.True(result.IsSuccess, result.Error);
        }
        await using var verify = CreateContext();
        Assert.Equal(AutomaticPaymentCheckStatus.NeedsReview,
            (await verify.AutomaticPaymentChecks.AsNoTracking().SingleAsync(c => c.Id == _firstReviewId)).Status);
        Assert.Equal(AutomaticPaymentCheckStatus.Scheduled,
            (await verify.AutomaticPaymentChecks.AsNoTracking().SingleAsync(c => c.Id == _scheduledId)).Status);
        Assert.Equal(100m, (await verify.Wallets.AsNoTracking().SingleAsync(w => w.Id == _walletId)).Balance);
        Assert.False(await verify.Transactions.AsNoTracking().AnyAsync(t => t.WalletId == _walletId));
    }

    [Fact]
    public void ReviewDto_DoesNotContainCheckoutSecretsOrLeaseToken()
    {
        var names = typeof(AutomaticPaymentReviewDto).GetProperties().Select(p => p.Name).ToArray();
        Assert.DoesNotContain("LeaseToken", names);
        Assert.DoesNotContain("AuthorizationUrl", names);
        Assert.DoesNotContain("AccessCode", names);
        Assert.DoesNotContain("CustomerEmail", names);
        Assert.DoesNotContain("GatewayResponse", names);
    }

    private PaymentIntent NewIntent(Guid userId, Guid walletId) => PaymentIntent.Create(
        userId, walletId, 50m, $"PAY-{Guid.NewGuid():N}", $"{userId:N}@tests.invalid", "Paystack");
    private AutomaticPaymentCheck NewReview(Guid intentId)
    {
        var check = AutomaticPaymentCheck.Schedule(intentId, _time, 1);
        var token = Guid.NewGuid();
        Assert.True(check.TryStart(token, _time, TimeSpan.FromMinutes(2)));
        check.MarkUnconfirmed(token, _time.AddMinutes(1), null);
        return check;
    }
    private GetAutomaticPaymentReviewQueueQuery Query() => new(1, 100, _customerId);
    private static GetAutomaticPaymentReviewQueueQueryHandler Handler(ErrandyDbContext db, ICurrentUser user) => new(db, user);
    private FakeCurrentUser Admin() => new(_adminId, true, UserRole.Admin);
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

    private sealed class FakeCurrentUser : ICurrentUser
    {
        public FakeCurrentUser(Guid? userId, bool authenticated, UserRole? role) { UserId = userId; IsAuthenticated = authenticated; Role = role; }
        public Guid? UserId { get; }
        public string? Email => null;
        public UserRole? Role { get; }
        public bool IsAuthenticated { get; }
    }
}
