using System;
using System.Linq;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using Errandy.Application.Common.Interfaces;
using Errandy.Application.Common.Results;
using Errandy.Application.Features.Payments.Queries.GetPendingPaymentReviews;
using Errandy.Domain.Entities;
using Errandy.Domain.Enums;
using Errandy.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Xunit;

namespace Errandy.Tests.Payments;

public class PendingPaymentReviewPostgresTests : IAsyncLifetime
{
    private readonly string _connection = GetSafeTestConnection();
    private readonly Guid _adminId = Guid.NewGuid();
    private readonly Guid _customerId = Guid.NewGuid();
    private readonly Guid _walletId = Guid.NewGuid();
    private readonly string _referenceOne = $"PAY-{Guid.NewGuid():N}";
    private readonly string _referenceTwo = $"PAY-{Guid.NewGuid():N}";
    private readonly string _completedReference = $"PAY-{Guid.NewGuid():N}";

    public async Task InitializeAsync()
    {
        await using var db = CreateContext();
        var phone = RandomNumberGenerator.GetInt32(10_000_000, 99_999_998);
        db.Users.Add(new User
        {
            Id = _adminId,
            FullName = "Review Test Admin",
            Email = $"{_adminId:N}@tests.invalid",
            PhoneNumber = "080" + phone.ToString(),
            PasswordHash = "test-only-not-a-real-password",
            Role = UserRole.Admin
        });
        db.Users.Add(new User
        {
            Id = _customerId,
            FullName = "Review Test Customer",
            Email = $"{_customerId:N}@tests.invalid",
            PhoneNumber = "080" + (phone + 1).ToString(),
            PasswordHash = "test-only-not-a-real-password",
            Role = UserRole.Customer
        });

        var first = NewIntent(_referenceOne);
        first.CreatedAt = new DateTime(1990, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var second = NewIntent(_referenceTwo);
        second.CreatedAt = new DateTime(1990, 1, 2, 0, 0, 0, DateTimeKind.Utc);
        var completed = NewIntent(_completedReference);
        completed.CreatedAt = new DateTime(1990, 1, 3, 0, 0, 0, DateTimeKind.Utc);
        var now = DateTime.UtcNow;
        completed.MarkSucceeded(Guid.NewGuid(), "test", "test", now, now);
        db.PaymentIntents.AddRange(first, second, completed);
        await db.SaveChangesAsync();
    }

    public async Task DisposeAsync()
    {
        await using var db = CreateContext();
        var intents = await db.PaymentIntents.Where(p => p.UserId == _customerId && p.WalletId == _walletId).ToListAsync();
        db.PaymentIntents.RemoveRange(intents);
        await db.SaveChangesAsync();
        var users = await db.Users.Where(u => u.Id == _adminId || u.Id == _customerId).ToListAsync();
        db.Users.RemoveRange(users);
        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task UnauthenticatedCaller_IsRejected()
    {
        await using var db = CreateContext();
        var handler = new GetPendingPaymentReviewsQueryHandler(db, new FakeCurrentUser(null, false, null));

        var result = await handler.Handle(new GetPendingPaymentReviewsQuery(), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(ErrorType.Unauthorized, result.ErrorType);
    }

    [Fact]
    public async Task CustomerWithAdminClaim_IsStillRejected()
    {
        await using var db = CreateContext();
        var handler = new GetPendingPaymentReviewsQueryHandler(db, new FakeCurrentUser(_customerId, true, UserRole.Admin));

        var result = await handler.Handle(new GetPendingPaymentReviewsQuery(), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(ErrorType.Forbidden, result.ErrorType);
    }

    [Fact]
    public async Task UnknownUserWithAdminClaim_IsRejected()
    {
        await using var db = CreateContext();
        var handler = new GetPendingPaymentReviewsQueryHandler(db, new FakeCurrentUser(Guid.NewGuid(), true, UserRole.Admin));

        var result = await handler.Handle(new GetPendingPaymentReviewsQuery(), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(ErrorType.Forbidden, result.ErrorType);
    }

    [Fact]
    public async Task Admin_SeesPendingButNotSucceededIntents()
    {
        await using var db = CreateContext();
        var handler = new GetPendingPaymentReviewsQueryHandler(db, new FakeCurrentUser(_adminId, true, UserRole.Customer));

        var result = await handler.Handle(new GetPendingPaymentReviewsQuery(1, 100), CancellationToken.None);

        Assert.True(result.IsSuccess, result.Error);
        Assert.NotNull(result.Value);
        Assert.Contains(result.Value!.Items, p => p.Reference == _referenceOne && p.UserId == _customerId);
        Assert.Contains(result.Value.Items, p => p.Reference == _referenceTwo && p.UserId == _customerId);
        Assert.DoesNotContain(result.Value.Items, p => p.Reference == _completedReference);
    }

    [Fact]
    public async Task Admin_PaginationReportsMore_WhenMorePendingPaymentsExist()
    {
        await using var db = CreateContext();
        var handler = new GetPendingPaymentReviewsQueryHandler(db, new FakeCurrentUser(_adminId, true, UserRole.Admin));

        var result = await handler.Handle(new GetPendingPaymentReviewsQuery(1, 1), CancellationToken.None);

        Assert.True(result.IsSuccess, result.Error);
        Assert.NotNull(result.Value);
        Assert.Single(result.Value!.Items);
        Assert.True(result.Value.HasMore);
        Assert.Equal(1, result.Value.Page);
        Assert.Equal(1, result.Value.PageSize);
    }

    [Theory]
    [InlineData(0, 10)]
    [InlineData(10001, 10)]
    [InlineData(1, 0)]
    [InlineData(1, 101)]
    public async Task InvalidPagination_IsRejected(int page, int pageSize)
    {
        await using var db = CreateContext();
        var handler = new GetPendingPaymentReviewsQueryHandler(db, new FakeCurrentUser(_adminId, true, UserRole.Admin));

        var result = await handler.Handle(new GetPendingPaymentReviewsQuery(page, pageSize), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(ErrorType.Validation, result.ErrorType);
    }

    private PaymentIntent NewIntent(string reference) => PaymentIntent.Create(
        _customerId, _walletId, 50m, reference, $"{_customerId:N}@tests.invalid", "Paystack");

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

    private sealed class FakeCurrentUser : ICurrentUser
    {
        public FakeCurrentUser(Guid? userId, bool authenticated, UserRole? claimedRole)
        {
            UserId = userId;
            IsAuthenticated = authenticated;
            Role = claimedRole;
        }
        public Guid? UserId { get; }
        public string? Email => null;
        public UserRole? Role { get; }
        public bool IsAuthenticated { get; }
    }
}
