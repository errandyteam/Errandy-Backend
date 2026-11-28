using System;
using System.Linq;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using Errandy.Application.Common.Interfaces;
using Errandy.Application.Features.Payments.Reconciliation;
using Errandy.Application.Features.Payments.Services;
using Errandy.Application.Features.Wallets.Commands.RequestWithdrawal;
using Errandy.Application.Interfaces;
using Errandy.Domain.Entities;
using Errandy.Domain.Enums;
using Errandy.Infrastructure.Payments;
using Errandy.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Xunit;
using WalletTransactionStatus = Errandy.Domain.Enums.TransactionStatus;

namespace Errandy.Tests.Payments;

// Uses only the guarded test DB and a fake gateway. The withdrawal here is an
// internal test record; this test does not initiate or prove a bank transfer.
public class PaymentWithdrawalConcurrencyPostgresTests : IAsyncLifetime
{
    private readonly string _connection = GetSafeTestConnection();
    private readonly Guid _userId = Guid.NewGuid();
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
            Id = _userId,
            FullName = "Concurrency Test Customer",
            Email = $"{_userId:N}@tests.invalid",
            PhoneNumber = "080" + phone.ToString(),
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
        var intent = PaymentIntent.Create(_userId, _walletId, 50m, _reference,
            $"{_userId:N}@tests.invalid", "Paystack");
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
        var withdrawals = await db.WithdrawalRequests.Where(w => w.UserId == _userId && w.WalletId == _walletId).ToListAsync();
        db.WithdrawalRequests.RemoveRange(withdrawals);
        await db.SaveChangesAsync();
        var intent = await db.PaymentIntents.SingleOrDefaultAsync(p => p.Id == _intentId && p.UserId == _userId);
        if (intent is not null) { db.PaymentIntents.Remove(intent); await db.SaveChangesAsync(); }
        var transactions = await db.Transactions.Where(t => t.WalletId == _walletId).ToListAsync();
        db.Transactions.RemoveRange(transactions);
        await db.SaveChangesAsync();
        var wallet = await db.Wallets.SingleOrDefaultAsync(w => w.Id == _walletId && w.UserId == _userId);
        if (wallet is not null) { db.Wallets.Remove(wallet); await db.SaveChangesAsync(); }
        var user = await db.Users.SingleOrDefaultAsync(u => u.Id == _userId);
        if (user is not null) { db.Users.Remove(user); await db.SaveChangesAsync(); }
    }

    [Fact]
    public async Task FundingAndWithdrawal_CompeteOnWallet_ThenFreshRetryPreservesBothLedgerEntries()
    {
        var barrier = new WalletSaveBarrier();
        var gateway = new FakeGateway(_clock);
        using var racingServices = CreateServices(gateway, barrier);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(25));

        async Task<bool> RunFundingAsync()
        {
            var result = await Executor(racingServices).ExecuteAsync(_checkId, timeout.Token);
            Assert.Equal(AutomaticPaymentClaimStatus.Claimed, result.ClaimStatus);
            Assert.True(result.Finalization == AutomaticPaymentFinalizationResult.Confirmed ||
                        result.Finalization == AutomaticPaymentFinalizationResult.Rescheduled);
            return result.Finalization == AutomaticPaymentFinalizationResult.Confirmed;
        }

        async Task<bool> RunWithdrawalAsync()
        {
            var options = new DbContextOptionsBuilder<ErrandyDbContext>().UseNpgsql(_connection).Options;
            await using var db = new BarrierContext(options, barrier);
            try
            {
                var result = await WithdrawalHandler(db).Handle(WithdrawalCommand(), timeout.Token);
                Assert.True(result.IsSuccess, result.Error);
                return true;
            }
            catch (DbUpdateConcurrencyException)
            {
                // Dispose this dirty scope; any retry must use a fresh context.
                return false;
            }
        }

        // Both handlers read and mutate the same wallet before either SaveChanges
        // proceeds. Exactly one first save can use the originally loaded xmin.
        var outcomes = await Task.WhenAll(RunFundingAsync(), RunWithdrawalAsync());
        var fundingSucceeded = outcomes[0];
        var withdrawalSucceeded = outcomes[1];
        Assert.NotEqual(fundingSucceeded, withdrawalSucceeded);
        Assert.Equal(1, gateway.Calls);

        // Retry only the unsuccessful operation, using entirely new scopes.
        if (!fundingSucceeded)
        {
            await using (var inspect = CreateContext())
            {
                var check = await inspect.AutomaticPaymentChecks.AsNoTracking().SingleAsync(c => c.Id == _checkId);
                Assert.Equal(AutomaticPaymentCheckStatus.Scheduled, check.Status);
                Assert.NotNull(check.NextAttemptAt);
                _clock.UtcNow = check.NextAttemptAt!.Value;
            }
            using var retryServices = CreateServices(gateway);
            var retry = await Executor(retryServices).ExecuteAsync(_checkId, timeout.Token);
            Assert.Equal((AutomaticPaymentFinalizationResult?)AutomaticPaymentFinalizationResult.Confirmed, retry.Finalization);
        }
        if (!withdrawalSucceeded)
        {
            await using var retryDb = CreateContext();
            var retry = await WithdrawalHandler(retryDb).Handle(WithdrawalCommand(), timeout.Token);
            Assert.True(retry.IsSuccess, retry.Error);
        }

        await using var verify = CreateContext();
        var wallet = await verify.Wallets.AsNoTracking().SingleAsync(w => w.Id == _walletId);
        var payment = await verify.PaymentIntents.AsNoTracking().SingleAsync(p => p.Id == _intentId);
        var checkFinal = await verify.AutomaticPaymentChecks.AsNoTracking().SingleAsync(c => c.Id == _checkId);
        var withdrawal = await verify.WithdrawalRequests.AsNoTracking().SingleAsync(w => w.UserId == _userId && w.WalletId == _walletId);
        var ledger = await verify.Transactions.AsNoTracking().Where(t => t.WalletId == _walletId).ToListAsync();
        Assert.Equal(130m, wallet.Balance);
        Assert.Equal(PaymentStatus.Succeeded, payment.Status);
        Assert.Equal(AutomaticPaymentCheckStatus.Confirmed, checkFinal.Status);
        // Current temporary request behavior always holds; no bank payout exists.
        Assert.Equal(WithdrawalStatus.PendingHold, withdrawal.Status);
        Assert.Equal(20m, withdrawal.Amount);
        Assert.Equal(2, ledger.Count);
        var credit = Assert.Single(ledger, t => t.Category == TransactionCategory.Funding);
        var debit = Assert.Single(ledger, t => t.Category == TransactionCategory.Withdrawal);
        Assert.Equal((Guid?)credit.Id, payment.TransactionId);
        Assert.Equal(withdrawal.TransactionId, debit.Id);
        Assert.Equal(TransactionType.Credit, credit.Type);
        Assert.Equal(TransactionType.Debit, debit.Type);
        Assert.Equal(WalletTransactionStatus.Completed, credit.Status);
        Assert.Equal(WalletTransactionStatus.Pending, debit.Status);
        Assert.Equal(50m, credit.Amount);
        Assert.Equal(20m, debit.Amount);
        Assert.Equal(_reference, credit.Reference);
        if (fundingSucceeded)
        {
            Assert.Equal(100m, credit.BalanceBefore); Assert.Equal(150m, credit.BalanceAfter);
            Assert.Equal(150m, debit.BalanceBefore); Assert.Equal(130m, debit.BalanceAfter);
        }
        else
        {
            Assert.Equal(100m, debit.BalanceBefore); Assert.Equal(80m, debit.BalanceAfter);
            Assert.Equal(80m, credit.BalanceBefore); Assert.Equal(130m, credit.BalanceAfter);
        }
    }

    private RequestWithdrawalCommandHandler WithdrawalHandler(IApplicationDbContext db) =>
        new(db, new FakeCurrentUser(_userId), _clock, new ConfigurationBuilder().Build());
    private static RequestWithdrawalCommand WithdrawalCommand() =>
        new(20m, "Test Bank", "0000000000", "Test Account", "000");

    private ServiceProvider CreateServices(IPaymentGateway gateway, WalletSaveBarrier? barrier = null)
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
        services.AddSingleton<IDateTimeProvider>(_clock);
        services.AddSingleton<IPaymentGateway>(gateway);
        services.AddScoped<IPaymentVerificationService, PaymentVerificationService>();
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
        private readonly IDateTimeProvider _clock;
        private int _calls;
        public FakeGateway(IDateTimeProvider clock) => _clock = clock;
        public string ProviderName => "Paystack";
        public int Calls => Volatile.Read(ref _calls);
        public Task<PaymentVerifyResult> VerifyPaymentAsync(string reference, CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref _calls);
            return Task.FromResult(new PaymentVerifyResult(true, "success", reference, 5000L, "NGN", "test", "test", _clock.UtcNow));
        }
        public Task<PaymentInitializeResult> InitializePaymentAsync(PaymentInitializeRequest request, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("Initialization is not used by this test.");
        public bool VerifyWebhookSignature(byte[] rawPayload, string signatureHeader) =>
            throw new InvalidOperationException("Webhooks are not used by this test.");
    }

    private sealed class WalletSaveBarrier
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
        private readonly WalletSaveBarrier _barrier;
        public BarrierContext(DbContextOptions<ErrandyDbContext> options, WalletSaveBarrier barrier) : base(options) => _barrier = barrier;
        public override async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
        {
            // Claims/finalization don't modify wallets and must not wait here.
            if (ChangeTracker.Entries<Wallet>().Any(e => e.State == EntityState.Modified))
                await _barrier.WaitAsync(cancellationToken);
            return await base.SaveChangesAsync(cancellationToken);
        }
    }
}
