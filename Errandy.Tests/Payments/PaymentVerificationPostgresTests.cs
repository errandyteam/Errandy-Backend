using System;
using System.Linq;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using Errandy.Application.Common.DTOs;
using Errandy.Application.Common.Interfaces;
using Errandy.Application.Common.Results;
using Errandy.Application.Features.Payments.Services;
using Errandy.Application.Interfaces;
using Errandy.Domain.Entities;
using Errandy.Domain.Enums;
using Errandy.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Xunit;

namespace Errandy.Tests.Payments;

public class PaymentVerificationPostgresTests : IAsyncLifetime
{
    private readonly string _connectionString = GetSafeTestConnection();
    private readonly Guid _userId = Guid.NewGuid();
    private readonly Guid _walletId = Guid.NewGuid();
    private readonly string _reference = $"PAY-{Guid.NewGuid():N}";
    private readonly FixedClock _clock = new();

    public async Task InitializeAsync()
    {
        await using var db = CreateContext();
        var phone = "080" + RandomNumberGenerator.GetInt32(10_000_000, 100_000_000).ToString();
        db.Users.Add(new User
        {
            Id = _userId,
            FullName = "Payment Verification Test",
            Email = $"{_userId:N}@tests.invalid",
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
        db.PaymentIntents.Add(PaymentIntent.Create(
            userId: _userId,
            walletId: _walletId,
            amount: 50m,
            reference: _reference,
            customerEmail: $"{_userId:N}@tests.invalid",
            provider: "Paystack"));
        await db.SaveChangesAsync();
    }

    public async Task DisposeAsync()
    {
        // Remove only records associated with this test instance's random IDs.
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
    public async Task SuccessfulPayment_IsCreditedOnce_WhenVerifiedAgain()
    {
        var gateway = new FakeGateway((reference, _) => Task.FromResult(Success(reference)));
        await using (var firstDb = CreateContext())
        {
            var first = await Service(firstDb, gateway).VerifyOwnedPaymentAsync(_reference, _userId);
            Assert.True(first.IsSuccess, first.Error);
            Assert.NotNull(first.Value);
            Assert.False(first.Value!.AlreadyCredited);
            Assert.Equal(150m, first.Value.WalletBalance);
        }

        await using (var secondDb = CreateContext())
        {
            var second = await Service(secondDb, gateway).VerifyOwnedPaymentAsync(_reference, _userId);
            Assert.True(second.IsSuccess, second.Error);
            Assert.NotNull(second.Value);
            Assert.True(second.Value!.AlreadyCredited);
            Assert.Equal(150m, second.Value.WalletBalance);
        }

        Assert.Equal(1, gateway.Calls);
        await AssertStateAsync(expectedBalance: 150m, expectedTransactions: 1, PaymentStatus.Succeeded);
    }

    [Theory]
    [InlineData("pending", "NGN", 5000L, false)]
    [InlineData("success", "USD", 5000L, false)]
    [InlineData("success", "NGN", 4999L, false)]
    [InlineData("success", "NGN", 5000L, true)]
    public async Task UnsuccessfulOrMismatchedGatewayDetails_DoNotCreditWallet(
        string status, string currency, long amountInKobo, bool wrongReference)
    {
        var gateway = new FakeGateway((reference, _) => Task.FromResult(new PaymentVerifyResult(
            IsSuccess: true,
            GatewayStatus: status,
            Reference: wrongReference ? reference + "-wrong" : reference,
            AmountInKobo: amountInKobo,
            Currency: currency,
            Channel: "test",
            GatewayResponse: "test",
            PaidAtUtc: _clock.UtcNow)));

        await using var db = CreateContext();
        var result = await Service(db, gateway).VerifyOwnedPaymentAsync(_reference, _userId);
        Assert.False(result.IsSuccess);
        Assert.Equal(1, gateway.Calls);
        await AssertStateAsync(expectedBalance: 100m, expectedTransactions: 0, PaymentStatus.Pending);
    }

    [Fact]
    public async Task AnotherUser_CannotVerifyPayment()
    {
        var gateway = new FakeGateway((reference, _) => Task.FromResult(Success(reference)));
        await using var db = CreateContext();
        var result = await Service(db, gateway).VerifyOwnedPaymentAsync(_reference, Guid.NewGuid());

        Assert.False(result.IsSuccess);
        Assert.Equal(ErrorType.NotFound, result.ErrorType);
        Assert.Equal(0, gateway.Calls);
        await AssertStateAsync(expectedBalance: 100m, expectedTransactions: 0, PaymentStatus.Pending);
    }

    [Fact]
    public async Task CompetingVerifications_ResultInOneCredit_AndRetryReturnsAlreadyCredited()
    {
        var arrived = 0;
        var bothAtGateway = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var gateway = new FakeGateway(async (reference, cancellationToken) =>
        {
            if (Interlocked.Increment(ref arrived) == 2)
                bothAtGateway.TrySetResult(true);
            await bothAtGateway.Task.WaitAsync(cancellationToken);
            return Success(reference);
        });

        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        var attempts = await Task.WhenAll(
            RunAttemptAsync(gateway, timeout.Token),
            RunAttemptAsync(gateway, timeout.Token));

        Assert.Equal(2, gateway.Calls);
        Assert.Contains(attempts, attempt => attempt.Result?.IsSuccess == true);
        foreach (var attempt in attempts)
        {
            if (attempt.Exception is not null)
            {
                // A concurrent duplicate can reach the database's unique reference index.
                var updateException = Assert.IsAssignableFrom<DbUpdateException>(attempt.Exception);
                var postgresException = Assert.IsType<PostgresException>(updateException.InnerException);
                Assert.Equal(PostgresErrorCodes.UniqueViolation, postgresException.SqlState);
                Assert.Equal("IX_Transactions_Reference", postgresException.ConstraintName);
            }
            else
            {
                Assert.NotNull(attempt.Result);
                Assert.True(attempt.Result!.IsSuccess || attempt.Result.ErrorType == ErrorType.Conflict);
            }
        }

        // Retry with a fresh context after both previous requests have ended.
        await using (var retryDb = CreateContext())
        {
            var retry = await Service(retryDb, gateway).VerifyOwnedPaymentAsync(_reference, _userId);
            Assert.True(retry.IsSuccess, retry.Error);
            Assert.NotNull(retry.Value);
            Assert.True(retry.Value!.AlreadyCredited);
        }
        await AssertStateAsync(expectedBalance: 150m, expectedTransactions: 1, PaymentStatus.Succeeded);
    }

    private async Task<Attempt> RunAttemptAsync(FakeGateway gateway, CancellationToken cancellationToken)
    {
        try
        {
            await using var db = CreateContext();
            var result = await Service(db, gateway).VerifyOwnedPaymentAsync(_reference, _userId, cancellationToken);
            return new Attempt(result, null);
        }
        catch (Exception ex)
        {
            return new Attempt(null, ex);
        }
    }

    private async Task AssertStateAsync(decimal expectedBalance, int expectedTransactions, PaymentStatus expectedStatus)
    {
        await using var db = CreateContext();
        var wallet = await db.Wallets.AsNoTracking().SingleAsync(w => w.Id == _walletId);
        var intent = await db.PaymentIntents.AsNoTracking().SingleAsync(p => p.Reference == _reference);
        var transactions = await db.Transactions.AsNoTracking().Where(t => t.Reference == _reference).ToListAsync();
        Assert.Equal(expectedBalance, wallet.Balance);
        Assert.Equal(expectedStatus, intent.Status);
        Assert.Equal(expectedTransactions, transactions.Count);
        if (expectedTransactions == 1)
        {
            var transaction = Assert.Single(transactions);
            Assert.Equal(_walletId, transaction.WalletId);
            Assert.Equal(50m, transaction.Amount);
            Assert.Equal(TransactionType.Credit, transaction.Type);
            Assert.Equal(TransactionCategory.Funding, transaction.Category);
            Assert.Equal((Guid?)transaction.Id, intent.TransactionId);
        }
    }

    private PaymentVerificationService Service(ErrandyDbContext db, IPaymentGateway gateway) =>
        new(db, gateway, _clock);

    private ErrandyDbContext CreateContext() => new(
        new DbContextOptionsBuilder<ErrandyDbContext>().UseNpgsql(_connectionString).Options);

    private PaymentVerifyResult Success(string reference) => new(
        IsSuccess: true,
        GatewayStatus: "success",
        Reference: reference,
        AmountInKobo: 5000L,
        Currency: "NGN",
        Channel: "test",
        GatewayResponse: "test approval",
        PaidAtUtc: _clock.UtcNow);

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

    private sealed class FixedClock : IDateTimeProvider
    {
        public DateTime UtcNow => new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
    }

    private sealed class FakeGateway : IPaymentGateway
    {
        private readonly Func<string, CancellationToken, Task<PaymentVerifyResult>> _verify;
        private int _calls;
        public FakeGateway(Func<string, CancellationToken, Task<PaymentVerifyResult>> verify) => _verify = verify;
        public string ProviderName => "Paystack";
        public int Calls => Volatile.Read(ref _calls);

        public Task<PaymentVerifyResult> VerifyPaymentAsync(string reference, CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref _calls);
            return _verify(reference, cancellationToken);
        }

        public Task<PaymentInitializeResult> InitializePaymentAsync(PaymentInitializeRequest request, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("This test must not initialize a payment.");
        public bool VerifyWebhookSignature(byte[] rawPayload, string signatureHeader) =>
            throw new InvalidOperationException("This test must not process webhooks.");
    }

    private record Attempt(Result<PaymentVerificationDto>? Result, Exception? Exception);
}
