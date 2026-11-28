using System;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using Errandy.Application.Common.Interfaces;
using Errandy.Application.Common.Results;
using Errandy.Application.Features.Payments.Commands.InitializePayment;
using Errandy.Domain.Entities;
using Errandy.Domain.Enums;
using Errandy.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Xunit;

namespace Errandy.Tests.Payments;

public class PaymentInitializationPostgresTests : IAsyncLifetime
{
    private readonly string _connection = GetSafeTestConnection();
    private readonly Guid _userId = Guid.NewGuid();
    private readonly Guid _walletId = Guid.NewGuid();
    private string Email => $"{_userId:N}@tests.invalid";

    public async Task InitializeAsync()
    {
        await using var db = CreateContext();
        var phone = "080" + RandomNumberGenerator.GetInt32(10_000_000, 100_000_000).ToString();
        db.Users.Add(new User
        {
            Id = _userId,
            FullName = "Payment Initialization Test",
            Email = Email,
            PhoneNumber = phone,
            PasswordHash = "test-only-not-a-real-password",
            Role = UserRole.Customer
        });
        db.Wallets.Add(new Wallet
        {
            Id = _walletId,
            UserId = _userId,
            Balance = 100m,
            LockedBalance = 0m,
            Currency = "NGN",
            IsActive = true
        });
        await db.SaveChangesAsync();
    }

    public async Task DisposeAsync()
    {
        // Delete only records associated with this test instance's random IDs.
        await using var db = CreateContext();
        var intents = await db.PaymentIntents.Where(p => p.UserId == _userId && p.WalletId == _walletId).ToListAsync();
        db.PaymentIntents.RemoveRange(intents);
        await db.SaveChangesAsync();
        var transactions = await db.Transactions.Where(t => t.WalletId == _walletId).ToListAsync();
        db.Transactions.RemoveRange(transactions);
        await db.SaveChangesAsync();
        var wallet = await db.Wallets.SingleOrDefaultAsync(w => w.Id == _walletId && w.UserId == _userId);
        if (wallet is not null)
        {
            db.Wallets.Remove(wallet);
            await db.SaveChangesAsync();
        }
        var user = await db.Users.SingleOrDefaultAsync(u => u.Id == _userId);
        if (user is not null)
        {
            db.Users.Remove(user);
            await db.SaveChangesAsync();
        }
    }

    [Fact]
    public async Task ValidInitialization_SavesIntentBeforeGateway_AndUsesKobo()
    {
        var gateway = new FakeGateway(async (request, ct) =>
        {
            // A different context can see the intent before the gateway returns.
            await using var check = CreateContext();
            var pending = await check.PaymentIntents.AsNoTracking()
                .SingleOrDefaultAsync(p => p.Reference == request.Reference, ct);
            Assert.NotNull(pending);
            Assert.Equal(_userId, pending!.UserId);
            Assert.Equal(_walletId, pending.WalletId);
            Assert.Equal(100.25m, pending.Amount);
            Assert.Equal(PaymentStatus.Pending, pending.Status);
            Assert.Null(pending.AuthorizationUrl);
            Assert.Equal(10025L, request.AmountInKobo);
            Assert.Equal(Email, request.CustomerEmail);
            return Success(request.Reference);
        });

        await using var db = CreateContext();
        var result = await Handler(db, gateway).Handle(new InitializePaymentCommand(100.25m), CancellationToken.None);

        Assert.True(result.IsSuccess, result.Error);
        Assert.NotNull(result.Value);
        Assert.Equal(1, gateway.Calls);
        Assert.Equal(100.25m, result.Value!.Amount);
        Assert.Equal("NGN", result.Value.Currency);
        await using var verify = CreateContext();
        var stored = await verify.PaymentIntents.AsNoTracking().SingleAsync(p => p.Id == result.Value.PaymentIntentId);
        Assert.Equal(result.Value.Reference, stored.Reference);
        Assert.Equal("https://checkout.paystack.com/test", stored.AuthorizationUrl);
        Assert.Equal("test-access-code", stored.AccessCode);
        Assert.Equal(PaymentStatus.Pending, stored.Status);
        Assert.Equal(100m, (await verify.Wallets.AsNoTracking().SingleAsync(w => w.Id == _walletId)).Balance);
        Assert.False(await verify.Transactions.AsNoTracking().AnyAsync(t => t.WalletId == _walletId));
    }

    [Fact]
    public async Task GatewayFailure_LeavesPendingIntent_WithoutCreditingWallet()
    {
        var gateway = new FakeGateway((_, _) => Task.FromResult(
            new PaymentInitializeResult(false, null, null, null, "Simulated error")));
        await using var db = CreateContext();
        var result = await Handler(db, gateway).Handle(new InitializePaymentCommand(50m), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(1, gateway.Calls);
        await using var verify = CreateContext();
        var intent = await verify.PaymentIntents.AsNoTracking().SingleAsync(p => p.UserId == _userId);
        Assert.Equal(PaymentStatus.Pending, intent.Status);
        Assert.Null(intent.AuthorizationUrl);
        Assert.Equal(100m, (await verify.Wallets.AsNoTracking().SingleAsync(w => w.Id == _walletId)).Balance);
        Assert.False(await verify.Transactions.AsNoTracking().AnyAsync(t => t.WalletId == _walletId));
    }

    [Theory]
    [InlineData("0")]
    [InlineData("-1")]
    [InlineData("0.001")]
    [InlineData("10000000.01")]
    public async Task InvalidAmount_DoesNotCreateIntent_OrCallGateway(string amount)
    {
        var gateway = new FakeGateway((request, _) => Task.FromResult(Success(request.Reference)));
        await using var db = CreateContext();
        var result = await Handler(db, gateway).Handle(
            new InitializePaymentCommand(decimal.Parse(amount, CultureInfo.InvariantCulture)), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(ErrorType.Validation, result.ErrorType);
        Assert.Equal(0, gateway.Calls);
        await AssertNoIntentAsync();
    }

    [Fact]
    public async Task UnauthenticatedUser_CannotInitializePayment()
    {
        var gateway = new FakeGateway((request, _) => Task.FromResult(Success(request.Reference)));
        await using var db = CreateContext();
        var handler = new InitializePaymentCommandHandler(db, new FakeCurrentUser(null, false), gateway);

        var result = await handler.Handle(new InitializePaymentCommand(50m), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(ErrorType.Unauthorized, result.ErrorType);
        Assert.Equal(0, gateway.Calls);
        await AssertNoIntentAsync();
    }

    [Fact]
    public async Task FrozenWallet_CannotInitializePayment()
    {
        await using (var setup = CreateContext())
        {
            var wallet = await setup.Wallets.SingleAsync(w => w.Id == _walletId);
            wallet.SetActiveStatus(false);
            await setup.SaveChangesAsync();
        }
        var gateway = new FakeGateway((request, _) => Task.FromResult(Success(request.Reference)));
        await using var db = CreateContext();
        var result = await Handler(db, gateway).Handle(new InitializePaymentCommand(50m), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(ErrorType.Forbidden, result.ErrorType);
        Assert.Equal(0, gateway.Calls);
        await AssertNoIntentAsync();
    }

    [Fact]
    public async Task NonNgnWallet_CannotInitializePayment()
    {
        await using (var setup = CreateContext())
        {
            var wallet = await setup.Wallets.SingleAsync(w => w.Id == _walletId);
            wallet.Currency = "USD";
            await setup.SaveChangesAsync();
        }
        var gateway = new FakeGateway((request, _) => Task.FromResult(Success(request.Reference)));
        await using var db = CreateContext();
        var result = await Handler(db, gateway).Handle(new InitializePaymentCommand(50m), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(ErrorType.Validation, result.ErrorType);
        Assert.Equal(0, gateway.Calls);
        await AssertNoIntentAsync();
    }

    [Fact]
    public async Task GatewayReturningWrongReference_DoesNotCompleteInitialization()
    {
        var gateway = new FakeGateway((_, _) => Task.FromResult(Success("PAY-wrong")));
        await using var db = CreateContext();
        var result = await Handler(db, gateway).Handle(new InitializePaymentCommand(50m), CancellationToken.None);

        Assert.False(result.IsSuccess);
        await using var verify = CreateContext();
        var intent = await verify.PaymentIntents.AsNoTracking().SingleAsync(p => p.UserId == _userId);
        Assert.Equal(PaymentStatus.Pending, intent.Status);
        Assert.Null(intent.AuthorizationUrl);
    }

    private async Task AssertNoIntentAsync()
    {
        await using var db = CreateContext();
        Assert.False(await db.PaymentIntents.AsNoTracking().AnyAsync(p => p.UserId == _userId));
    }

    private InitializePaymentCommandHandler Handler(ErrandyDbContext db, IPaymentGateway gateway) =>
        new(db, new FakeCurrentUser(_userId, true), gateway);

    private static PaymentInitializeResult Success(string reference) => new(
        IsSuccess: true,
        Reference: reference,
        AuthorizationUrl: "https://checkout.paystack.com/test",
        AccessCode: "test-access-code");

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
        public FakeCurrentUser(Guid? userId, bool authenticated)
        {
            UserId = userId;
            IsAuthenticated = authenticated;
        }
        public Guid? UserId { get; }
        public string? Email => null;
        public UserRole? Role => UserRole.Customer;
        public bool IsAuthenticated { get; }
    }

    private sealed class FakeGateway : IPaymentGateway
    {
        private readonly Func<PaymentInitializeRequest, CancellationToken, Task<PaymentInitializeResult>> _initialize;
        public int Calls { get; private set; }
        public FakeGateway(Func<PaymentInitializeRequest, CancellationToken, Task<PaymentInitializeResult>> initialize) =>
            _initialize = initialize;
        public string ProviderName => "Paystack";
        public Task<PaymentInitializeResult> InitializePaymentAsync(PaymentInitializeRequest request, CancellationToken cancellationToken = default)
        {
            Calls++;
            return _initialize(request, cancellationToken);
        }
        public Task<PaymentVerifyResult> VerifyPaymentAsync(string reference, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("Initialization tests must not verify a payment.");
        public bool VerifyWebhookSignature(byte[] rawPayload, string signatureHeader) =>
            throw new InvalidOperationException("Initialization tests must not process webhooks.");
    }
}
