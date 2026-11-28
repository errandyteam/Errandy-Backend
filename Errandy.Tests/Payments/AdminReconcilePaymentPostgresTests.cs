using System;
using System.Linq;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using Errandy.Application.Common.DTOs;
using Errandy.Application.Common.Interfaces;
using Errandy.Application.Common.Results;
using Errandy.Application.Features.Payments.Commands.AdminReconcilePayment;
using Errandy.Application.Features.Payments.Services;
using Errandy.Application.Interfaces;
using Errandy.Domain.Entities;
using Errandy.Domain.Enums;
using Errandy.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Xunit;
using TransactionEntity = Errandy.Domain.Entities.Transaction;
using WalletTransactionStatus = Errandy.Domain.Enums.TransactionStatus;

namespace Errandy.Tests.Payments;

public class AdminReconcilePaymentPostgresTests : IAsyncLifetime
{
    private readonly string _connection = GetSafeTestConnection();
    private readonly Guid _adminId = Guid.NewGuid();
    private readonly Guid _customerId = Guid.NewGuid();
    private readonly Guid _walletId = Guid.NewGuid();
    private readonly string _reference = $"PAY-{Guid.NewGuid():N}";
    private readonly FixedClock _clock = new();
    private Guid _intentId;

    public async Task InitializeAsync()
    {
        await using var db = CreateContext();
        var phone = RandomNumberGenerator.GetInt32(10_000_000, 99_999_998);
        db.Users.Add(new User
        {
            Id = _adminId,
            FullName = "Reconciliation Test Admin",
            Email = $"{_adminId:N}@tests.invalid",
            PhoneNumber = "080" + phone.ToString(),
            PasswordHash = "test-only-not-a-real-password",
            Role = UserRole.Admin
        });
        db.Users.Add(new User
        {
            Id = _customerId,
            FullName = "Reconciliation Test Customer",
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
        // Delete only records created by this test instance, in foreign-key order.
        await using var db = CreateContext();
        var audits = await db.PaymentReconciliationAttempts
            .Where(a => a.PaymentIntentId == _intentId && a.AdminUserId == _adminId).ToListAsync();
        db.PaymentReconciliationAttempts.RemoveRange(audits);
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
    public async Task UnauthenticatedCaller_CannotStartRetry()
    {
        var gateway = SuccessfulGateway();
        using var provider = CreateServices(gateway);
        var handler = Handler(provider, new FakeCurrentUser(null, false, null));

        var result = await handler.Handle(Command(), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(ErrorType.Unauthorized, result.ErrorType);
        Assert.Equal(0, gateway.Calls);
        await AssertStateAsync(100m, PaymentStatus.Pending, 0, 0);
    }

    [Fact]
    public async Task CustomerWithAdminClaim_CannotStartRetry()
    {
        var gateway = SuccessfulGateway();
        using var provider = CreateServices(gateway);
        var handler = Handler(provider, new FakeCurrentUser(_customerId, true, UserRole.Admin));

        var result = await handler.Handle(Command(), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(ErrorType.Forbidden, result.ErrorType);
        Assert.Equal(0, gateway.Calls);
        await AssertStateAsync(100m, PaymentStatus.Pending, 0, 0);
    }

    [Fact]
    public async Task MissingReason_IsRejectedBeforeAuditOrGateway()
    {
        var gateway = SuccessfulGateway();
        using var provider = CreateServices(gateway);
        var handler = Handler(provider, Admin());

        var result = await handler.Handle(new AdminReconcilePaymentCommand(_reference, "  "), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(ErrorType.Validation, result.ErrorType);
        Assert.Equal(0, gateway.Calls);
        await AssertStateAsync(100m, PaymentStatus.Pending, 0, 0);
    }

    [Fact]
    public async Task UnknownReference_IsRejectedBeforeAuditOrGateway()
    {
        var gateway = SuccessfulGateway();
        using var provider = CreateServices(gateway);
        var handler = Handler(provider, Admin());

        var result = await handler.Handle(new AdminReconcilePaymentCommand($"UNKNOWN-{Guid.NewGuid():N}", "Review request"), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(ErrorType.NotFound, result.ErrorType);
        Assert.Equal(0, gateway.Calls);
        await AssertStateAsync(100m, PaymentStatus.Pending, 0, 0);
    }

    [Fact]
    public async Task SuccessfulRetry_RecordsAuditBeforeGateway_AndRepeatDoesNotCreditAgain()
    {
        var gateway = new FakeGateway(async (reference, ct) =>
        {
            await using var check = CreateContext();
            var attempt = await check.PaymentReconciliationAttempts.AsNoTracking()
                .SingleAsync(a => a.PaymentIntentId == _intentId && a.AdminUserId == _adminId, ct);
            Assert.Equal(PaymentReconciliationOutcome.Started, attempt.Outcome);
            Assert.Equal("Customer reported missing credit", attempt.Reason);
            return Success(reference);
        });
        using var provider = CreateServices(gateway);
        var handler = Handler(provider, Admin());

        var first = await handler.Handle(Command(), CancellationToken.None);
        Assert.True(first.IsSuccess, first.Error);
        Assert.NotNull(first.Value);
        Assert.False(first.Value!.Payment.AlreadyCredited);
        await using (var check = CreateContext())
        {
            var audit = await check.PaymentReconciliationAttempts.AsNoTracking().SingleAsync(a => a.Id == first.Value.AuditAttemptId);
            Assert.Equal(PaymentReconciliationOutcome.Succeeded, audit.Outcome);
            Assert.Equal("Credited", audit.ResultCode);
        }

        var second = await handler.Handle(Command(), CancellationToken.None);
        Assert.True(second.IsSuccess, second.Error);
        Assert.NotNull(second.Value);
        Assert.True(second.Value!.Payment.AlreadyCredited);
        Assert.NotEqual(first.Value.AuditAttemptId, second.Value.AuditAttemptId);
        Assert.Equal(1, gateway.Calls);
        await using (var check = CreateContext())
        {
            var audit = await check.PaymentReconciliationAttempts.AsNoTracking().SingleAsync(a => a.Id == second.Value.AuditAttemptId);
            Assert.Equal(PaymentReconciliationOutcome.Succeeded, audit.Outcome);
            Assert.Equal("AlreadyCredited", audit.ResultCode);
        }
        await AssertStateAsync(150m, PaymentStatus.Succeeded, 1, 2);
    }

    [Fact]
    public async Task Retry_UsesIndependentCommits_WhenCallerHasAmbientTransaction()
    {
        var gateway = SuccessfulGateway();
        using var provider = CreateServices(gateway);
        using (var outer = new System.Transactions.TransactionScope(
            System.Transactions.TransactionScopeOption.Required,
            System.Transactions.TransactionScopeAsyncFlowOption.Enabled))
        {
            var result = await Handler(provider, Admin()).Handle(Command(), CancellationToken.None);
            Assert.True(result.IsSuccess, result.Error);
            // Deliberately do not complete the caller's transaction.
        }

        await AssertStateAsync(150m, PaymentStatus.Succeeded, 1, 1);
    }

    [Fact]
    public async Task MismatchedAmount_IsRecordedAsUnknown_WithoutCredit()
    {
        var gateway = new FakeGateway((reference, _) => Task.FromResult(new PaymentVerifyResult(
            true, "success", reference, 4999L, "NGN", "test", "test", _clock.UtcNow)));
        using var provider = CreateServices(gateway);
        var result = await Handler(provider, Admin()).Handle(Command(), CancellationToken.None);

        Assert.False(result.IsSuccess);
        await using var check = CreateContext();
        var audit = await check.PaymentReconciliationAttempts.AsNoTracking().SingleAsync(a => a.PaymentIntentId == _intentId);
        Assert.Equal(PaymentReconciliationOutcome.Unknown, audit.Outcome);
        Assert.Equal("VerificationUnconfirmed", audit.ResultCode);
        await AssertStateAsync(100m, PaymentStatus.Pending, 0, 1);
    }

    [Fact]
    public async Task InterruptedVerification_IsRecordedAsUnknown()
    {
        var gateway = new FakeGateway((_, _) => Task.FromException<PaymentVerifyResult>(
            new InvalidOperationException("Simulated test interruption")));
        using var provider = CreateServices(gateway);

        await Assert.ThrowsAsync<InvalidOperationException>(
            async () => await Handler(provider, Admin()).Handle(Command(), CancellationToken.None));

        await using var check = CreateContext();
        var audit = await check.PaymentReconciliationAttempts.AsNoTracking().SingleAsync(a => a.PaymentIntentId == _intentId);
        Assert.Equal(PaymentReconciliationOutcome.Unknown, audit.Outcome);
        Assert.Equal("VerificationInterrupted", audit.ResultCode);
        await AssertStateAsync(100m, PaymentStatus.Pending, 0, 1);
    }

    [Fact]
    public async Task DirtyFailedVerifier_DoesNotGetSavedWhenAuditIsFinalized()
    {
        var gateway = SuccessfulGateway();
        using var provider = CreateServices(gateway, sp =>
            new DirtyFailingVerifier(sp.GetRequiredService<IApplicationDbContext>(), _walletId, _reference));

        var result = await Handler(provider, Admin()).Handle(Command(), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(ErrorType.Conflict, result.ErrorType);
        await using var check = CreateContext();
        var audit = await check.PaymentReconciliationAttempts.AsNoTracking().SingleAsync(a => a.PaymentIntentId == _intentId);
        Assert.Equal(PaymentReconciliationOutcome.Unknown, audit.Outcome);
        Assert.Equal("VerificationUnconfirmed", audit.ResultCode);
        await AssertStateAsync(100m, PaymentStatus.Pending, 0, 1);
    }

    [Fact]
    public async Task FailedResult_DoesNotClaimPaymentIsUncredited_WhenAnotherFlowCompletedIt()
    {
        var gateway = SuccessfulGateway();
        using var provider = CreateServices(gateway, sp =>
            new ConcurrentCompletionThenFailureVerifier(
                sp.GetRequiredService<IServiceScopeFactory>(), _reference, _customerId));

        var result = await Handler(provider, Admin()).Handle(Command(), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(ErrorType.Conflict, result.ErrorType);
        await using var check = CreateContext();
        var audit = await check.PaymentReconciliationAttempts.AsNoTracking()
            .SingleAsync(a => a.PaymentIntentId == _intentId);
        Assert.Equal(PaymentReconciliationOutcome.Unknown, audit.Outcome);
        Assert.Equal("VerificationUnconfirmed", audit.ResultCode);
        await AssertStateAsync(150m, PaymentStatus.Succeeded, 1, 1);
    }

    private async Task AssertStateAsync(decimal balance, PaymentStatus status, int transactions, int audits)
    {
        await using var db = CreateContext();
        Assert.Equal(balance, (await db.Wallets.AsNoTracking().SingleAsync(w => w.Id == _walletId)).Balance);
        Assert.Equal(status, (await db.PaymentIntents.AsNoTracking().SingleAsync(p => p.Id == _intentId)).Status);
        Assert.Equal(transactions, await db.Transactions.AsNoTracking().CountAsync(t => t.WalletId == _walletId));
        Assert.Equal(audits, await db.PaymentReconciliationAttempts.AsNoTracking().CountAsync(a => a.PaymentIntentId == _intentId));
    }

    private ServiceProvider CreateServices(
        IPaymentGateway gateway,
        Func<IServiceProvider, IPaymentVerificationService>? verifierFactory = null)
    {
        var services = new ServiceCollection();
        services.AddDbContext<ErrandyDbContext>(options => options.UseNpgsql(_connection));
        services.AddScoped<IApplicationDbContext>(sp => sp.GetRequiredService<ErrandyDbContext>());
        services.AddSingleton<IPaymentGateway>(gateway);
        services.AddSingleton<IDateTimeProvider>(_clock);
        if (verifierFactory is null)
            services.AddScoped<IPaymentVerificationService, PaymentVerificationService>();
        else
            services.AddScoped<IPaymentVerificationService>(verifierFactory);
        return services.BuildServiceProvider(validateScopes: true);
    }

    private AdminReconcilePaymentCommandHandler Handler(ServiceProvider provider, ICurrentUser currentUser) =>
        new(currentUser, provider.GetRequiredService<IServiceScopeFactory>(), _clock);
    private FakeCurrentUser Admin() => new(_adminId, true, UserRole.Admin);
    private AdminReconcilePaymentCommand Command() => new(_reference, "Customer reported missing credit");
    private FakeGateway SuccessfulGateway() => new((reference, _) => Task.FromResult(Success(reference)));
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
        public FakeCurrentUser(Guid? userId, bool authenticated, UserRole? role) { UserId = userId; IsAuthenticated = authenticated; Role = role; }
        public Guid? UserId { get; }
        public string? Email => null;
        public UserRole? Role { get; }
        public bool IsAuthenticated { get; }
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
            throw new InvalidOperationException("This test must not initialize payments.");
        public bool VerifyWebhookSignature(byte[] rawPayload, string signatureHeader) =>
            throw new InvalidOperationException("This test must not process webhooks.");
    }

    // Simulate a different request completing in its own scope while this
    // attempt receives a generic failure. The failure cannot describe global state.
    private sealed class ConcurrentCompletionThenFailureVerifier : IPaymentVerificationService
    {
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly string _reference;
        private readonly Guid _customerId;

        public ConcurrentCompletionThenFailureVerifier(IServiceScopeFactory scopeFactory, string reference, Guid customerId)
        {
            _scopeFactory = scopeFactory; _reference = reference; _customerId = customerId;
        }

        public async Task<Result<PaymentVerificationDto>> VerifyOwnedPaymentAsync(
            string reference, Guid userId, CancellationToken cancellationToken = default)
        {
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<IApplicationDbContext>();
            var gateway = scope.ServiceProvider.GetRequiredService<IPaymentGateway>();
            var clock = scope.ServiceProvider.GetRequiredService<IDateTimeProvider>();
            var otherFlow = new PaymentVerificationService(db, gateway, clock);
            var completed = await otherFlow.VerifyOwnedPaymentAsync(_reference, _customerId, cancellationToken);
            if (!completed.IsSuccess)
                throw new InvalidOperationException("The simulated other flow did not complete.");
            return Result<PaymentVerificationDto>.Failure("Simulated conflict", ErrorType.Conflict);
        }
    }

    private sealed class DirtyFailingVerifier : IPaymentVerificationService
    {
        private readonly IApplicationDbContext _db;
        private readonly Guid _walletId;
        private readonly string _reference;
        public DirtyFailingVerifier(IApplicationDbContext db, Guid walletId, string reference)
        {
            _db = db; _walletId = walletId; _reference = reference;
        }
        public async Task<Result<PaymentVerificationDto>> VerifyOwnedPaymentAsync(
            string reference, Guid userId, CancellationToken cancellationToken = default)
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
            return Result<PaymentVerificationDto>.Failure("Simulated conflict", ErrorType.Conflict);
        }
    }
}
