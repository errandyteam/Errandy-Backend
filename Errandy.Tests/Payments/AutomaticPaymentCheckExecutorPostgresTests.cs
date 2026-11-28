using System;
using System.Linq;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using Errandy.Application.Common.DTOs;
using Errandy.Application.Common.Interfaces;
using Errandy.Application.Common.Results;
using Errandy.Application.Features.Payments.Reconciliation;
using Errandy.Application.Features.Payments.Commands.VerifyPayment;
using Errandy.Application.Features.Payments.Services;
using Errandy.Application.Interfaces;
using Errandy.Domain.Entities;
using Errandy.Domain.Enums;
using Errandy.Infrastructure.Payments;
using Errandy.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Xunit;
using TransactionEntity = Errandy.Domain.Entities.Transaction;
using WalletTransactionStatus = Errandy.Domain.Enums.TransactionStatus;

namespace Errandy.Tests.Payments;

// Explicit single-check execution, real PostgreSQL and shared verifier with a
// fake gateway. Does not start Program.cs, a hosted worker, or call Paystack.
public class AutomaticPaymentCheckExecutorPostgresTests : IAsyncLifetime
{
    private readonly string _connection = GetSafeTestConnection();
    private readonly Guid _customerId = Guid.NewGuid();
    private readonly Guid _walletId = Guid.NewGuid();
    private readonly string _reference = $"PAY-{Guid.NewGuid():N}";
    private readonly FixedClock _clock = new(new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc));
    private Guid _intentId;
    private Guid _checkId;

    public async Task InitializeAsync()
    {
        await using var db = CreateContext();
        var phone = RandomNumberGenerator.GetInt32(10_000_000, 100_000_000);
        db.Users.Add(new User
        {
            Id = _customerId,
            FullName = "Executor Test Customer",
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
        var intent = PaymentIntent.Create(_customerId, _walletId, 50m, _reference,
            $"{_customerId:N}@tests.invalid", "Paystack");
        _intentId = intent.Id;
        db.PaymentIntents.Add(intent);
        await db.SaveChangesAsync();
        var check = AutomaticPaymentCheck.Schedule(_intentId, _clock.UtcNow, 2);
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
        var transactions = await db.Transactions.Where(t => t.WalletId == _walletId).ToListAsync();
        db.Transactions.RemoveRange(transactions);
        await db.SaveChangesAsync();
        var wallet = await db.Wallets.SingleOrDefaultAsync(w => w.Id == _walletId && w.UserId == _customerId);
        if (wallet is not null) { db.Wallets.Remove(wallet); await db.SaveChangesAsync(); }
        var user = await db.Users.SingleOrDefaultAsync(u => u.Id == _customerId);
        if (user is not null) { db.Users.Remove(user); await db.SaveChangesAsync(); }
    }

    [Fact]
    public async Task SuccessfulExecution_ClaimsBeforeGateway_AndCreditsOnlyOnce()
    {
        var gateway = new FakeGateway(async (reference, ct) =>
        {
            await using var db = CreateContext();
            var check = await db.AutomaticPaymentChecks.AsNoTracking().SingleAsync(c => c.Id == _checkId, ct);
            Assert.Equal(AutomaticPaymentCheckStatus.Leased, check.Status);
            Assert.Equal(1, check.AttemptsStarted);
            return Success(reference);
        });
        using var provider = CreateServices(gateway);
        var executor = Executor(provider);
        var first = await executor.ExecuteAsync(_checkId);
        var second = await executor.ExecuteAsync(_checkId);
        Assert.Equal(AutomaticPaymentClaimStatus.Claimed, first.ClaimStatus);
        Assert.Equal((AutomaticPaymentFinalizationResult?)AutomaticPaymentFinalizationResult.Confirmed, first.Finalization);
        Assert.True(first.VerifierCalled);
        Assert.Equal(AutomaticPaymentClaimStatus.NotAvailable, second.ClaimStatus);
        Assert.False(second.VerifierCalled);
        Assert.Equal(1, gateway.Calls);
        await AssertStateAsync(150m, PaymentStatus.Succeeded, 1, AutomaticPaymentCheckStatus.Confirmed);
    }

    [Fact]
    public async Task GatewayFailure_ReschedulesWithoutCredit()
    {
        var gateway = new FakeGateway((_, _) => Task.FromResult(new PaymentVerifyResult(false, null, null, null, null, null, null, null)));
        using var provider = CreateServices(gateway);
        var result = await Executor(provider).ExecuteAsync(_checkId);
        Assert.Equal((AutomaticPaymentFinalizationResult?)AutomaticPaymentFinalizationResult.Rescheduled, result.Finalization);
        Assert.True(result.VerifierCalled);
        await AssertStateAsync(100m, PaymentStatus.Pending, 0, AutomaticPaymentCheckStatus.Scheduled);
    }

    [Fact]
    public async Task MismatchedGatewayAmount_ReschedulesWithoutCredit()
    {
        var gateway = new FakeGateway((reference, _) => Task.FromResult(new PaymentVerifyResult(true, "success", reference, 4999L, "NGN", "test", "test", _clock.UtcNow)));
        using var provider = CreateServices(gateway);
        var result = await Executor(provider).ExecuteAsync(_checkId);
        Assert.Equal((AutomaticPaymentFinalizationResult?)AutomaticPaymentFinalizationResult.Rescheduled, result.Finalization);
        await AssertStateAsync(100m, PaymentStatus.Pending, 0, AutomaticPaymentCheckStatus.Scheduled);
    }

    [Fact]
    public async Task GatewayException_RecordsUnconfirmedAndPropagates()
    {
        var gateway = new FakeGateway((_, _) => Task.FromException<PaymentVerifyResult>(new InvalidOperationException("Synthetic interruption")));
        using var provider = CreateServices(gateway);
        await Assert.ThrowsAsync<InvalidOperationException>(() => Executor(provider).ExecuteAsync(_checkId));
        await AssertStateAsync(100m, PaymentStatus.Pending, 0, AutomaticPaymentCheckStatus.Scheduled);
        await using var db = CreateContext();
        Assert.Equal(AutomaticPaymentCheckResult.Unconfirmed,
            (await db.AutomaticPaymentChecks.AsNoTracking().SingleAsync(c => c.Id == _checkId)).LastResult);
    }

    [Fact]
    public async Task CallerCancellationDuringGateway_StillAttemptsIndependentFinalization()
    {
        using var cancellation = new CancellationTokenSource();
        var gateway = new FakeGateway((_, ct) =>
        {
            cancellation.Cancel();
            ct.ThrowIfCancellationRequested();
            throw new InvalidOperationException("Cancellation was expected.");
        });
        using var provider = CreateServices(gateway);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Executor(provider).ExecuteAsync(_checkId, cancellation.Token));
        await AssertStateAsync(100m, PaymentStatus.Pending, 0, AutomaticPaymentCheckStatus.Scheduled);
    }

    [Fact]
    public async Task DirtyFailedVerifierScope_IsNotSavedByFinalization()
    {
        var gateway = new FakeGateway((reference, _) => Task.FromResult(Success(reference)));
        using var provider = CreateServices(gateway, sp => new DirtyFailingVerifier(
            sp.GetRequiredService<IApplicationDbContext>(), _walletId, _reference));
        var result = await Executor(provider).ExecuteAsync(_checkId);
        Assert.Equal((AutomaticPaymentFinalizationResult?)AutomaticPaymentFinalizationResult.Rescheduled, result.Finalization);
        Assert.True(result.VerifierCalled);
        Assert.Equal(0, gateway.Calls);
        await AssertStateAsync(100m, PaymentStatus.Pending, 0, AutomaticPaymentCheckStatus.Scheduled);
    }

    [Fact]
    public async Task LeaseExpiresDuringVerification_PaymentMaySucceedButLeaseIsNotFinalized()
    {
        var gateway = new FakeGateway((reference, _) =>
        {
            _clock.UtcNow = _clock.UtcNow.AddMinutes(2);
            return Task.FromResult(Success(reference));
        });
        using var provider = CreateServices(gateway);
        var result = await Executor(provider).ExecuteAsync(_checkId);
        Assert.Equal((AutomaticPaymentFinalizationResult?)AutomaticPaymentFinalizationResult.LeaseLost, result.Finalization);
        Assert.True(result.VerifierCalled);
        await AssertStateAsync(150m, PaymentStatus.Succeeded, 1, AutomaticPaymentCheckStatus.Leased);
        // A future recovery/retry must use the verifier's idempotent already-credited path.
    }

    [Fact]
    public async Task NonPendingOrSucceededPayment_DoesNotCallVerifier()
    {
        await using (var db = CreateContext())
        {
            var intent = await db.PaymentIntents.SingleAsync(p => p.Id == _intentId);
            intent.MarkFailed("test-only", _clock.UtcNow);
            await db.SaveChangesAsync();
        }
        var gateway = new FakeGateway((reference, _) => Task.FromResult(Success(reference)));
        using var provider = CreateServices(gateway);
        var result = await Executor(provider).ExecuteAsync(_checkId);
        Assert.False(result.VerifierCalled);
        Assert.Equal(0, gateway.Calls);
        Assert.Equal((AutomaticPaymentFinalizationResult?)AutomaticPaymentFinalizationResult.Rescheduled, result.Finalization);
        await AssertStateAsync(100m, PaymentStatus.Failed, 0, AutomaticPaymentCheckStatus.Scheduled);
    }

    [Fact]
    public async Task ExecutionCommits_AreIndependentOfCallerAmbientTransaction()
    {
        var gateway = new FakeGateway((reference, _) => Task.FromResult(Success(reference)));
        using var provider = CreateServices(gateway);
        using (var outer = new System.Transactions.TransactionScope(
            System.Transactions.TransactionScopeOption.Required,
            System.Transactions.TransactionScopeAsyncFlowOption.Enabled))
        {
            var result = await Executor(provider).ExecuteAsync(_checkId);
            Assert.Equal((AutomaticPaymentFinalizationResult?)AutomaticPaymentFinalizationResult.Confirmed, result.Finalization);
            // Caller deliberately does not complete its transaction.
        }
        await AssertStateAsync(150m, PaymentStatus.Succeeded, 1, AutomaticPaymentCheckStatus.Confirmed);
    }

    [Fact]
    public async Task MissingCheck_IsNotClaimedAndNeverCallsGateway()
    {
        var gateway = new FakeGateway((reference, _) => Task.FromResult(Success(reference)));
        using var provider = CreateServices(gateway);
        var result = await Executor(provider).ExecuteAsync(Guid.NewGuid());
        Assert.Equal(AutomaticPaymentClaimStatus.NotAvailable, result.ClaimStatus);
        Assert.Null(result.Finalization);
        Assert.False(result.VerifierCalled);
        Assert.Equal(0, gateway.Calls);
        await AssertStateAsync(100m, PaymentStatus.Pending, 0, AutomaticPaymentCheckStatus.Scheduled);
    }

    [Fact]
    public async Task AutomaticCheckAndUserVerification_CompeteWithoutDoubleCredit()
    {
        var arrivals = 0;
        var bothAtGateway = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var gateway = new FakeGateway(async (reference, ct) =>
        {
            if (Interlocked.Increment(ref arrivals) == 2) bothAtGateway.TrySetResult(true);
            await bothAtGateway.Task.WaitAsync(TimeSpan.FromSeconds(15), ct);
            return Success(reference);
        });
        using var provider = CreateServices(gateway);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(25));

        async Task RunAutomaticAsync()
        {
            try
            {
                var result = await Executor(provider).ExecuteAsync(_checkId, timeout.Token);
                Assert.Equal(AutomaticPaymentClaimStatus.Claimed, result.ClaimStatus);
                Assert.True(result.Finalization == AutomaticPaymentFinalizationResult.Confirmed ||
                            result.Finalization == AutomaticPaymentFinalizationResult.Rescheduled);
            }
            catch (DbUpdateException ex)
            {
                AssertExpectedDuplicateReference(ex);
            }
        }

        async Task RunUserAsync()
        {
            try
            {
                await using var db = CreateContext();
                var verifier = new PaymentVerificationService(db, gateway, _clock);
                var handler = new VerifyPaymentCommandHandler(new FakeCurrentUser(_customerId), verifier);
                var result = await handler.Handle(new VerifyPaymentCommand(_reference), timeout.Token);
                Assert.True(result.IsSuccess || result.ErrorType == ErrorType.Conflict);
            }
            catch (DbUpdateException ex)
            {
                AssertExpectedDuplicateReference(ex);
            }
        }

        // Both real verification flows enter the fake gateway before either can
        // proceed. Their later database interleaving is intentionally not fixed.
        await Task.WhenAll(RunAutomaticAsync(), RunUserAsync());
        Assert.Equal(2, gateway.Calls);

        await using (var db = CreateContext())
        {
            var wallet = await db.Wallets.AsNoTracking().SingleAsync(w => w.Id == _walletId);
            var payment = await db.PaymentIntents.AsNoTracking().SingleAsync(p => p.Id == _intentId);
            var transaction = await db.Transactions.AsNoTracking().SingleAsync(t => t.Reference == _reference);
            Assert.Equal(150m, wallet.Balance);
            Assert.Equal(PaymentStatus.Succeeded, payment.Status);
            Assert.Equal((Guid?)transaction.Id, payment.TransactionId);
            Assert.Equal(_walletId, transaction.WalletId);
            Assert.Equal(50m, transaction.Amount);
            Assert.Equal(TransactionType.Credit, transaction.Type);
            Assert.Equal(TransactionCategory.Funding, transaction.Category);
        }

        AutomaticPaymentCheck current;
        await using (var db = CreateContext())
            current = await db.AutomaticPaymentChecks.AsNoTracking().SingleAsync(c => c.Id == _checkId);
        Assert.True(current.Status == AutomaticPaymentCheckStatus.Confirmed || current.Status == AutomaticPaymentCheckStatus.Scheduled);
        if (current.Status == AutomaticPaymentCheckStatus.Scheduled)
        {
            Assert.NotNull(current.NextAttemptAt);
            _clock.UtcNow = current.NextAttemptAt!.Value;
            var retry = await Executor(provider).ExecuteAsync(_checkId, timeout.Token);
            Assert.Equal((AutomaticPaymentFinalizationResult?)AutomaticPaymentFinalizationResult.Confirmed, retry.Finalization);
        }
        Assert.Equal(2, gateway.Calls); // Already-credited retry needs no gateway call.
        await AssertStateAsync(150m, PaymentStatus.Succeeded, 1, AutomaticPaymentCheckStatus.Confirmed);
    }

    private static void AssertExpectedDuplicateReference(DbUpdateException error)
    {
        var postgres = Assert.IsType<PostgresException>(error.InnerException);
        Assert.Equal(PostgresErrorCodes.UniqueViolation, postgres.SqlState);
        Assert.Equal("IX_Transactions_Reference", postgres.ConstraintName);
    }

    private async Task AssertStateAsync(decimal balance, PaymentStatus paymentStatus, int transactionCount, AutomaticPaymentCheckStatus checkStatus)
    {
        await using var db = CreateContext();
        Assert.Equal(balance, (await db.Wallets.AsNoTracking().SingleAsync(w => w.Id == _walletId)).Balance);
        Assert.Equal(paymentStatus, (await db.PaymentIntents.AsNoTracking().SingleAsync(p => p.Id == _intentId)).Status);
        Assert.Equal(transactionCount, await db.Transactions.AsNoTracking().CountAsync(t => t.WalletId == _walletId));
        Assert.Equal(checkStatus, (await db.AutomaticPaymentChecks.AsNoTracking().SingleAsync(c => c.Id == _checkId)).Status);
    }

    private ServiceProvider CreateServices(IPaymentGateway gateway, Func<IServiceProvider, IPaymentVerificationService>? verifierFactory = null)
    {
        var services = new ServiceCollection();
        services.AddDbContext<ErrandyDbContext>(options => options.UseNpgsql(_connection));
        services.AddScoped<IApplicationDbContext>(sp => sp.GetRequiredService<ErrandyDbContext>());
        services.AddSingleton<IPaymentGateway>(gateway);
        services.AddSingleton<IDateTimeProvider>(_clock);
        if (verifierFactory is null) services.AddScoped<IPaymentVerificationService, PaymentVerificationService>();
        else services.AddScoped<IPaymentVerificationService>(verifierFactory);
        return services.BuildServiceProvider(validateScopes: true);
    }

    private AutomaticPaymentCheckExecutor Executor(ServiceProvider provider)
    {
        var factory = provider.GetRequiredService<IServiceScopeFactory>();
        var policy = new AutomaticPaymentRetryPolicy();
        return new AutomaticPaymentCheckExecutor(factory, _clock,
            new AutomaticPaymentCheckClaimer(factory, _clock, TimeSpan.FromMinutes(2)),
            new AutomaticPaymentCheckFinalizer(factory, _clock, policy));
    }

    private PaymentVerifyResult Success(string reference) => new(true, "success", reference, 5000L, "NGN", "test", "test", _clock.UtcNow);
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
        public FakeCurrentUser(Guid userId) => UserId = userId;
        public Guid? UserId { get; }
        public string? Email => null;
        public UserRole? Role => UserRole.Customer;
        public bool IsAuthenticated => true;
    }

    private sealed class FixedClock : IDateTimeProvider
    {
        public FixedClock(DateTime now) => UtcNow = now;
        public DateTime UtcNow { get; set; }
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
            throw new InvalidOperationException("Initialization is not used by this test.");
        public bool VerifyWebhookSignature(byte[] rawPayload, string signatureHeader) =>
            throw new InvalidOperationException("Webhooks are not used by this test.");
    }

    private sealed class DirtyFailingVerifier : IPaymentVerificationService
    {
        private readonly IApplicationDbContext _db;
        private readonly Guid _walletId;
        private readonly string _reference;
        public DirtyFailingVerifier(IApplicationDbContext db, Guid walletId, string reference)
        { _db = db; _walletId = walletId; _reference = reference; }
        public async Task<Result<PaymentVerificationDto>> VerifyOwnedPaymentAsync(string reference, Guid userId, CancellationToken cancellationToken = default)
        {
            var wallet = await _db.Wallets.SingleAsync(w => w.Id == _walletId, cancellationToken);
            wallet.Credit(77m);
            _db.Transactions.Add(new TransactionEntity
            {
                WalletId = _walletId,
                Type = TransactionType.Credit,
                Category = TransactionCategory.Funding,
                Amount = 77m,
                BalanceBefore = 100m,
                BalanceAfter = 177m,
                Status = WalletTransactionStatus.Completed,
                Reference = _reference,
                Description = "Unsaved test change"
            });
            return Result<PaymentVerificationDto>.Failure("Synthetic conflict", ErrorType.Conflict);
        }
    }
}
