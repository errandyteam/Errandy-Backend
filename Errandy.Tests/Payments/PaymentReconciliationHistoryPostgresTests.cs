using System;
using System.Linq;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using Errandy.Application.Common.Interfaces;
using Errandy.Application.Common.Results;
using Errandy.Application.Features.Payments.Queries.GetPaymentReconciliationHistory;
using Errandy.Domain.Entities;
using Errandy.Domain.Enums;
using Errandy.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Xunit;

namespace Errandy.Tests.Payments;

public class PaymentReconciliationHistoryPostgresTests : IAsyncLifetime
{
    private readonly string _connection = GetSafeTestConnection();
    private readonly Guid _adminId = Guid.NewGuid();
    private readonly Guid _customerId = Guid.NewGuid();
    private readonly Guid _walletId = Guid.NewGuid();
    private readonly string _reference = $"PAY-{Guid.NewGuid():N}";
    private readonly string _otherReference = $"PAY-{Guid.NewGuid():N}";
    private readonly string _emptyReference = $"PAY-{Guid.NewGuid():N}";
    private Guid _intentId;
    private Guid _otherIntentId;
    private Guid _emptyIntentId;
    private Guid _startedId;
    private Guid _unknownId;
    private Guid _successId;
    private readonly DateTime _firstTime = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    public async Task InitializeAsync()
    {
        await using var db = CreateContext();
        var phone = RandomNumberGenerator.GetInt32(10_000_000, 99_999_998);
        db.Users.Add(new User
        {
            Id = _adminId,
            FullName = "History Test Admin",
            Email = $"{_adminId:N}@tests.invalid",
            PhoneNumber = "080" + phone.ToString(),
            PasswordHash = "test-only-not-a-real-password",
            Role = UserRole.Admin
        });
        db.Users.Add(new User
        {
            Id = _customerId,
            FullName = "History Test Customer",
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
        var intent = NewIntent(_reference);
        var other = NewIntent(_otherReference);
        var empty = NewIntent(_emptyReference);
        _intentId = intent.Id;
        _otherIntentId = other.Id;
        _emptyIntentId = empty.Id;
        db.PaymentIntents.AddRange(intent, other, empty);
        await db.SaveChangesAsync();

        var started = PaymentReconciliationAttempt.Start(_intentId, _adminId, _reference, "First review", _firstTime);
        var unknown = PaymentReconciliationAttempt.Start(_intentId, _adminId, _reference, "Second review", _firstTime.AddMinutes(1));
        unknown.Finish(PaymentReconciliationOutcome.Unknown, "VerificationUnconfirmed", _firstTime.AddMinutes(2));
        var success = PaymentReconciliationAttempt.Start(_intentId, _adminId, _reference, "Third review", _firstTime.AddMinutes(1));
        success.Finish(PaymentReconciliationOutcome.Succeeded, "AlreadyCredited", _firstTime.AddMinutes(2));
        var otherAttempt = PaymentReconciliationAttempt.Start(_otherIntentId, _adminId, _otherReference, "Other payment", _firstTime.AddMinutes(3));
        _startedId = started.Id;
        _unknownId = unknown.Id;
        _successId = success.Id;
        db.PaymentReconciliationAttempts.AddRange(started, unknown, success, otherAttempt);
        await db.SaveChangesAsync();
    }

    public async Task DisposeAsync()
    {
        // Clean up only this test instance's rows, in foreign-key order.
        await using var db = CreateContext();
        var attempts = await db.PaymentReconciliationAttempts
            .Where(a => a.AdminUserId == _adminId && (a.PaymentIntentId == _intentId || a.PaymentIntentId == _otherIntentId || a.PaymentIntentId == _emptyIntentId)).ToListAsync();
        db.PaymentReconciliationAttempts.RemoveRange(attempts);
        await db.SaveChangesAsync();
        var intents = await db.PaymentIntents.Where(p => p.UserId == _customerId &&
            (p.Id == _intentId || p.Id == _otherIntentId || p.Id == _emptyIntentId)).ToListAsync();
        db.PaymentIntents.RemoveRange(intents);
        await db.SaveChangesAsync();
        var wallet = await db.Wallets.SingleOrDefaultAsync(w => w.Id == _walletId && w.UserId == _customerId);
        if (wallet is not null) { db.Wallets.Remove(wallet); await db.SaveChangesAsync(); }
        var users = await db.Users.Where(u => u.Id == _adminId || u.Id == _customerId).ToListAsync();
        db.Users.RemoveRange(users);
        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task Unauthenticated_IsRejected()
    {
        await using var db = CreateContext();
        var result = await Handler(db, new FakeCurrentUser(null, false, null)).Handle(
            new GetPaymentReconciliationHistoryQuery(_reference), CancellationToken.None);
        Assert.False(result.IsSuccess);
        Assert.Equal(ErrorType.Unauthorized, result.ErrorType);
    }

    [Fact]
    public async Task CustomerClaimingAdmin_IsRejectedBeforeReferenceLookup()
    {
        await using var db = CreateContext();
        var result = await Handler(db, new FakeCurrentUser(_customerId, true, UserRole.Admin)).Handle(
            new GetPaymentReconciliationHistoryQuery("UNKNOWN-REFERENCE"), CancellationToken.None);
        Assert.False(result.IsSuccess);
        Assert.Equal(ErrorType.Forbidden, result.ErrorType);
    }

    [Fact]
    public async Task MissingUserClaimingAdmin_IsRejected()
    {
        await using var db = CreateContext();
        var result = await Handler(db, new FakeCurrentUser(Guid.NewGuid(), true, UserRole.Admin)).Handle(
            new GetPaymentReconciliationHistoryQuery(_reference), CancellationToken.None);
        Assert.False(result.IsSuccess);
        Assert.Equal(ErrorType.Forbidden, result.ErrorType);
    }

    [Fact]
    public async Task DatabaseAdmin_SeesOnlySelectedPaymentHistory_WithExpectedFields()
    {
        await using var db = CreateContext();
        // The database role, not a potentially stale role claim, is authoritative.
        var result = await Handler(db, new FakeCurrentUser(_adminId, true, UserRole.Customer)).Handle(
            new GetPaymentReconciliationHistoryQuery("  " + _reference + "  ", 1, 50), CancellationToken.None);

        Assert.True(result.IsSuccess, result.Error);
        Assert.NotNull(result.Value);
        var page = result.Value!;
        Assert.Equal(3, page.Items.Count);
        Assert.False(page.HasMore);
        Assert.All(page.Items, item =>
        {
            Assert.Equal(_intentId, item.PaymentIntentId);
            Assert.Equal(_adminId, item.AdminUserId);
            Assert.Equal(_reference, item.Reference);
        });
        var started = Assert.Single(page.Items, a => a.AttemptId == _startedId);
        Assert.Equal("First review", started.Reason);
        Assert.Equal(PaymentReconciliationOutcome.Started, started.Outcome);
        Assert.Equal("Started", started.OutcomeName);
        Assert.Null(started.ResultCode);
        Assert.Null(started.FinishedAt);
        Assert.Equal(_firstTime, started.RequestedAt);
        var unknown = Assert.Single(page.Items, a => a.AttemptId == _unknownId);
        Assert.Equal(PaymentReconciliationOutcome.Unknown, unknown.Outcome);
        Assert.Equal("VerificationUnconfirmed", unknown.ResultCode);
        Assert.Equal((DateTime?)_firstTime.AddMinutes(2), unknown.FinishedAt);
        var success = Assert.Single(page.Items, a => a.AttemptId == _successId);
        Assert.Equal(PaymentReconciliationOutcome.Succeeded, success.Outcome);
        Assert.Equal("AlreadyCredited", success.ResultCode);
    }

    [Fact]
    public async Task Pagination_IsStableAcrossEqualTimestamps_AndReportsHasMore()
    {
        await using var db = CreateContext();
        var handler = Handler(db, Admin());
        var all = await handler.Handle(new GetPaymentReconciliationHistoryQuery(_reference, 1, 10), CancellationToken.None);
        var first = await handler.Handle(new GetPaymentReconciliationHistoryQuery(_reference, 1, 1), CancellationToken.None);
        var second = await handler.Handle(new GetPaymentReconciliationHistoryQuery(_reference, 2, 1), CancellationToken.None);
        var third = await handler.Handle(new GetPaymentReconciliationHistoryQuery(_reference, 3, 1), CancellationToken.None);
        var fourth = await handler.Handle(new GetPaymentReconciliationHistoryQuery(_reference, 4, 1), CancellationToken.None);

        Assert.True(all.IsSuccess, all.Error);
        Assert.True(first.IsSuccess, first.Error);
        Assert.True(second.IsSuccess, second.Error);
        Assert.True(third.IsSuccess, third.Error);
        Assert.True(fourth.IsSuccess, fourth.Error);
        Assert.NotNull(all.Value); Assert.NotNull(first.Value); Assert.NotNull(second.Value);
        Assert.NotNull(third.Value); Assert.NotNull(fourth.Value);
        var pages = new[] { Assert.Single(first.Value!.Items), Assert.Single(second.Value!.Items), Assert.Single(third.Value!.Items) };
        Assert.Equal(all.Value!.Items.Select(a => a.AttemptId).ToArray(), pages.Select(a => a.AttemptId).ToArray());
        Assert.Equal(3, pages.Select(a => a.AttemptId).Distinct().Count());
        Assert.True(pages[0].RequestedAt >= pages[1].RequestedAt);
        Assert.True(pages[1].RequestedAt >= pages[2].RequestedAt);
        Assert.True(first.Value.HasMore);
        Assert.True(second.Value.HasMore);
        Assert.False(third.Value.HasMore);
        Assert.Empty(fourth.Value!.Items);
        Assert.False(fourth.Value.HasMore);
        Assert.Equal(2, second.Value.Page);
        Assert.Equal(1, second.Value.PageSize);
    }

    [Fact]
    public async Task ExistingPaymentWithoutAttempts_ReturnsEmptyHistory()
    {
        await using var db = CreateContext();
        var result = await Handler(db, Admin()).Handle(new GetPaymentReconciliationHistoryQuery(_emptyReference), CancellationToken.None);
        Assert.True(result.IsSuccess, result.Error);
        Assert.NotNull(result.Value);
        Assert.Empty(result.Value!.Items);
        Assert.False(result.Value.HasMore);
    }

    [Fact]
    public async Task UnknownPayment_ReturnsNotFound()
    {
        await using var db = CreateContext();
        var result = await Handler(db, Admin()).Handle(new GetPaymentReconciliationHistoryQuery($"UNKNOWN-{Guid.NewGuid():N}"), CancellationToken.None);
        Assert.False(result.IsSuccess);
        Assert.Equal(ErrorType.NotFound, result.ErrorType);
    }

    [Theory]
    [InlineData(0, 10)]
    [InlineData(10001, 10)]
    [InlineData(1, 0)]
    [InlineData(1, 101)]
    public async Task InvalidPagination_IsRejected(int page, int pageSize)
    {
        await using var db = CreateContext();
        var result = await Handler(db, Admin()).Handle(new GetPaymentReconciliationHistoryQuery(_reference, page, pageSize), CancellationToken.None);
        Assert.False(result.IsSuccess);
        Assert.Equal(ErrorType.Validation, result.ErrorType);
    }

    [Fact]
    public async Task InvalidReferences_AreRejected()
    {
        await using var db = CreateContext();
        var handler = Handler(db, Admin());
        var empty = await handler.Handle(new GetPaymentReconciliationHistoryQuery("  "), CancellationToken.None);
        var tooLong = await handler.Handle(new GetPaymentReconciliationHistoryQuery(new string('x', 101)), CancellationToken.None);
        Assert.False(empty.IsSuccess);
        Assert.Equal(ErrorType.Validation, empty.ErrorType);
        Assert.False(tooLong.IsSuccess);
        Assert.Equal(ErrorType.Validation, tooLong.ErrorType);
    }

    [Fact]
    public async Task HistoryRead_DoesNotModifyPaymentWalletOrAudit()
    {
        await using (var db = CreateContext())
        {
            var result = await Handler(db, Admin()).Handle(new GetPaymentReconciliationHistoryQuery(_reference), CancellationToken.None);
            Assert.True(result.IsSuccess, result.Error);
        }
        await using var check = CreateContext();
        Assert.Equal(100m, (await check.Wallets.AsNoTracking().SingleAsync(w => w.Id == _walletId)).Balance);
        Assert.Equal(PaymentStatus.Pending, (await check.PaymentIntents.AsNoTracking().SingleAsync(p => p.Id == _intentId)).Status);
        Assert.False(await check.Transactions.AsNoTracking().AnyAsync(t => t.WalletId == _walletId));
        var audits = await check.PaymentReconciliationAttempts.AsNoTracking().Where(a => a.PaymentIntentId == _intentId).ToListAsync();
        Assert.Equal(3, audits.Count);
        Assert.Equal(PaymentReconciliationOutcome.Started, audits.Single(a => a.Id == _startedId).Outcome);
        Assert.Equal(PaymentReconciliationOutcome.Unknown, audits.Single(a => a.Id == _unknownId).Outcome);
        Assert.Equal(PaymentReconciliationOutcome.Succeeded, audits.Single(a => a.Id == _successId).Outcome);
    }

    private PaymentIntent NewIntent(string reference) => PaymentIntent.Create(
        _customerId, _walletId, 50m, reference, $"{_customerId:N}@tests.invalid", "Paystack");
    private GetPaymentReconciliationHistoryQueryHandler Handler(ErrandyDbContext db, ICurrentUser user) => new(db, user);
    private FakeCurrentUser Admin() => new(_adminId, true, UserRole.Admin);
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

    private sealed class FakeCurrentUser : ICurrentUser
    {
        public FakeCurrentUser(Guid? userId, bool authenticated, UserRole? role)
        { UserId = userId; IsAuthenticated = authenticated; Role = role; }
        public Guid? UserId { get; }
        public string? Email => null;
        public UserRole? Role { get; }
        public bool IsAuthenticated { get; }
    }
}
