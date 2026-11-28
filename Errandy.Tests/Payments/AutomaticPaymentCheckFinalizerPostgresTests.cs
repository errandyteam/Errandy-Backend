using System;
using System.Linq;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using Errandy.Application.Common.DTOs;
using Errandy.Application.Common.Interfaces;
using Errandy.Application.Features.Payments.Reconciliation;
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

// Finalizer tests only. Success fixtures are synthetic; no gateway or worker runs.
public class AutomaticPaymentCheckFinalizerPostgresTests : IAsyncLifetime
{
    private readonly string _connection = GetSafeTestConnection();
    private readonly Guid _customerId = Guid.NewGuid();
    private readonly Guid _walletId = Guid.NewGuid();
    private readonly string _reference = $"PAY-{Guid.NewGuid():N}";
    private readonly DateTime _start = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
    private Guid _intentId;
    private Guid _checkId;
    private Guid _token;
    private AutomaticPaymentLease Lease => new(_checkId, _intentId, _token, _start.AddMinutes(2), 1, 2);

    public async Task InitializeAsync()
    {
        await using var db = CreateContext();
        var phone = RandomNumberGenerator.GetInt32(10_000_000, 100_000_000);
        db.Users.Add(new User
        {
            Id = _customerId, FullName = "Finalizer Test Customer", Email = $"{_customerId:N}@tests.invalid",
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
        var check = AutomaticPaymentCheck.Schedule(_intentId, _start, 2);
        _checkId = check.Id;
        _token = Guid.NewGuid();
        Assert.True(check.TryStart(_token, _start, TimeSpan.FromMinutes(2)));
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
    public async Task UnconfirmedResult_ReschedulesWithoutChangingMoney()
    {
        using var provider = CreateServices();
        var result = await Finalizer(provider, _start.AddMinutes(1)).FinalizeAsync(Lease, null);
        Assert.Equal(AutomaticPaymentFinalizationResult.Rescheduled, result);
        await using var db = CreateContext();
        var check = await db.AutomaticPaymentChecks.AsNoTracking().SingleAsync(c => c.Id == _checkId);
        Assert.Equal(AutomaticPaymentCheckStatus.Scheduled, check.Status);
        Assert.Equal(AutomaticPaymentCheckResult.Unconfirmed, check.LastResult);
        Assert.Equal((DateTime?)_start.AddMinutes(6), check.NextAttemptAt);
        Assert.Equal(1, check.AttemptsStarted);
        Assert.Equal(2L, check.Revision);
        Assert.Null(check.LeaseToken);
        Assert.Equal(PaymentStatus.Pending, (await db.PaymentIntents.AsNoTracking().SingleAsync(p => p.Id == _intentId)).Status);
        Assert.Equal(100m, (await db.Wallets.AsNoTracking().SingleAsync(w => w.Id == _walletId)).Balance);
        Assert.False(await db.Transactions.AsNoTracking().AnyAsync(t => t.WalletId == _walletId));
    }

    [Fact]
    public async Task MatchingSuccessAndPersistedFunding_IsConfirmedWithoutAnotherCredit()
    {
        var dto = await InsertSyntheticSuccessAsync();
        using var provider = CreateServices();
        var result = await Finalizer(provider, _start.AddMinutes(1)).FinalizeAsync(Lease, dto);
        Assert.Equal(AutomaticPaymentFinalizationResult.Confirmed, result);
        await using var db = CreateContext();
        var check = await db.AutomaticPaymentChecks.AsNoTracking().SingleAsync(c => c.Id == _checkId);
        Assert.Equal(AutomaticPaymentCheckStatus.Confirmed, check.Status);
        Assert.Equal(AutomaticPaymentCheckResult.Confirmed, check.LastResult);
        Assert.Null(check.LeaseToken);
        Assert.Null(check.NextAttemptAt);
        Assert.Equal(150m, (await db.Wallets.AsNoTracking().SingleAsync(w => w.Id == _walletId)).Balance);
        Assert.Equal(1, await db.Transactions.AsNoTracking().CountAsync(t => t.WalletId == _walletId));
    }

    [Fact]
    public async Task SuccessValueWithoutPersistedSuccess_IsUnconfirmed()
    {
        var fabricated = Dto(Guid.NewGuid());
        using var provider = CreateServices();
        var result = await Finalizer(provider, _start.AddMinutes(1)).FinalizeAsync(Lease, fabricated);
        Assert.Equal(AutomaticPaymentFinalizationResult.Rescheduled, result);
        await using var db = CreateContext();
        var check = await db.AutomaticPaymentChecks.AsNoTracking().SingleAsync(c => c.Id == _checkId);
        Assert.Equal(AutomaticPaymentCheckResult.Unconfirmed, check.LastResult);
        Assert.Equal(PaymentStatus.Pending, (await db.PaymentIntents.AsNoTracking().SingleAsync(p => p.Id == _intentId)).Status);
    }

    [Fact]
    public async Task SucceededIntentWithoutMatchingLedger_IsUnconfirmed()
    {
        var transactionId = Guid.NewGuid();
        await using (var db = CreateContext())
        {
            var intent = await db.PaymentIntents.SingleAsync(p => p.Id == _intentId);
            intent.MarkSucceeded(transactionId, "test", "test", _start, _start);
            await db.SaveChangesAsync();
        }
        using var provider = CreateServices();
        var result = await Finalizer(provider, _start.AddMinutes(1)).FinalizeAsync(Lease, Dto(transactionId));
        Assert.Equal(AutomaticPaymentFinalizationResult.Rescheduled, result);
        await using var verify = CreateContext();
        var check = await verify.AutomaticPaymentChecks.AsNoTracking().SingleAsync(c => c.Id == _checkId);
        Assert.Equal(AutomaticPaymentCheckResult.Unconfirmed, check.LastResult);
        Assert.False(await verify.Transactions.AsNoTracking().AnyAsync(t => t.WalletId == _walletId));
    }

    [Fact]
    public async Task MismatchedSuccessValue_RemainsUnconfirmedEvenWhenPaymentSucceeded()
    {
        var dto = await InsertSyntheticSuccessAsync();
        using var provider = CreateServices();
        var result = await Finalizer(provider, _start.AddMinutes(1)).FinalizeAsync(Lease, dto with { Reference = "WRONG-REFERENCE" });
        Assert.Equal(AutomaticPaymentFinalizationResult.Rescheduled, result);
        await using var db = CreateContext();
        var check = await db.AutomaticPaymentChecks.AsNoTracking().SingleAsync(c => c.Id == _checkId);
        Assert.Equal(AutomaticPaymentCheckResult.Unconfirmed, check.LastResult);
        Assert.Equal(PaymentStatus.Succeeded, (await db.PaymentIntents.AsNoTracking().SingleAsync(p => p.Id == _intentId)).Status);
        Assert.Equal(150m, (await db.Wallets.AsNoTracking().SingleAsync(w => w.Id == _walletId)).Balance);
    }

    [Fact]
    public async Task LastUnconfirmedAttempt_GoesToReview()
    {
        var next = _start.AddMinutes(6);
        var secondToken = Guid.NewGuid();
        await using (var db = CreateContext())
        {
            var check = await db.AutomaticPaymentChecks.SingleAsync(c => c.Id == _checkId);
            check.MarkUnconfirmed(_token, _start.AddMinutes(1), next);
            Assert.True(check.TryStart(secondToken, next, TimeSpan.FromMinutes(2)));
            await db.SaveChangesAsync();
        }
        var secondLease = new AutomaticPaymentLease(_checkId, _intentId, secondToken, next.AddMinutes(2), 2, 2);
        using var provider = CreateServices();
        var result = await Finalizer(provider, next.AddMinutes(1)).FinalizeAsync(secondLease, null);
        Assert.Equal(AutomaticPaymentFinalizationResult.NeedsReview, result);
        await using var verify = CreateContext();
        var stored = await verify.AutomaticPaymentChecks.AsNoTracking().SingleAsync(c => c.Id == _checkId);
        Assert.Equal(AutomaticPaymentCheckStatus.NeedsReview, stored.Status);
        Assert.Null(stored.NextAttemptAt);
        Assert.Equal(PaymentStatus.Pending, (await verify.PaymentIntents.AsNoTracking().SingleAsync(p => p.Id == _intentId)).Status);
    }

    [Fact]
    public async Task WrongTokenOrExpiredLease_IsRejectedWithoutMutation()
    {
        using var provider = CreateServices();
        var wrong = await Finalizer(provider, _start.AddMinutes(1)).FinalizeAsync(Lease with { Token = Guid.NewGuid() }, null);
        var expired = await Finalizer(provider, _start.AddMinutes(2)).FinalizeAsync(Lease, null);
        Assert.Equal(AutomaticPaymentFinalizationResult.LeaseLost, wrong);
        Assert.Equal(AutomaticPaymentFinalizationResult.LeaseLost, expired);
        await using var db = CreateContext();
        var check = await db.AutomaticPaymentChecks.AsNoTracking().SingleAsync(c => c.Id == _checkId);
        Assert.Equal(AutomaticPaymentCheckStatus.Leased, check.Status);
        Assert.Equal((Guid?)_token, check.LeaseToken);
        Assert.Equal(1L, check.Revision);
    }

    [Fact]
    public async Task OldLeaseAfterRecovery_CannotFinalize()
    {
        await using (var db = CreateContext())
        {
            var check = await db.AutomaticPaymentChecks.SingleAsync(c => c.Id == _checkId);
            check.RecoverExpiredLease(_start.AddMinutes(2), _start.AddMinutes(7));
            await db.SaveChangesAsync();
        }
        using var provider = CreateServices();
        Assert.Equal(AutomaticPaymentFinalizationResult.LeaseLost,
            await Finalizer(provider, _start.AddMinutes(1)).FinalizeAsync(Lease, null));
        await using var verify = CreateContext();
        Assert.Equal(AutomaticPaymentCheckStatus.Scheduled,
            (await verify.AutomaticPaymentChecks.AsNoTracking().SingleAsync(c => c.Id == _checkId)).Status);
    }

    [Fact]
    public async Task TwoFinalizersAfterSameRead_ExactlyOneCommits()
    {
        var barrier = new SaveBarrier();
        using var provider = CreateServices(barrier);
        var one = Finalizer(provider, _start.AddMinutes(1));
        var two = Finalizer(provider, _start.AddMinutes(1));
        var results = await Task.WhenAll(one.FinalizeAsync(Lease, null), two.FinalizeAsync(Lease, null));
        Assert.Equal(1, results.Count(r => r == AutomaticPaymentFinalizationResult.Rescheduled));
        Assert.Equal(1, results.Count(r => r == AutomaticPaymentFinalizationResult.Contended));
        await using var db = CreateContext();
        var check = await db.AutomaticPaymentChecks.AsNoTracking().SingleAsync(c => c.Id == _checkId);
        Assert.Equal(2L, check.Revision);
        Assert.Equal(AutomaticPaymentCheckStatus.Scheduled, check.Status);
    }

    [Fact]
    public async Task FinalizationCommit_IsIndependentOfCallerAmbientTransaction()
    {
        using var provider = CreateServices();
        using (var outer = new System.Transactions.TransactionScope(
            System.Transactions.TransactionScopeOption.Required,
            System.Transactions.TransactionScopeAsyncFlowOption.Enabled))
        {
            Assert.Equal(AutomaticPaymentFinalizationResult.Rescheduled,
                await Finalizer(provider, _start.AddMinutes(1)).FinalizeAsync(Lease, null));
            // Deliberately do not complete the caller's transaction.
        }
        await using var verify = CreateContext();
        Assert.Equal(AutomaticPaymentCheckStatus.Scheduled,
            (await verify.AutomaticPaymentChecks.AsNoTracking().SingleAsync(c => c.Id == _checkId)).Status);
    }

    [Fact]
    public async Task CancelledFinalization_LeavesLeaseForLaterRecovery()
    {
        using var provider = CreateServices();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            Finalizer(provider, _start.AddMinutes(1)).FinalizeAsync(Lease, null, cancellation.Token));
        await using var db = CreateContext();
        var check = await db.AutomaticPaymentChecks.AsNoTracking().SingleAsync(c => c.Id == _checkId);
        Assert.Equal(AutomaticPaymentCheckStatus.Leased, check.Status);
        Assert.Equal((Guid?)_token, check.LeaseToken);
    }

    private async Task<PaymentVerificationDto> InsertSyntheticSuccessAsync()
    {
        await using var db = CreateContext();
        var wallet = await db.Wallets.SingleAsync(w => w.Id == _walletId);
        wallet.Credit(50m);
        var transaction = new TransactionEntity
        {
            WalletId = _walletId, Type = TransactionType.Credit, Category = TransactionCategory.Funding,
            Amount = 50m, BalanceBefore = 100m, BalanceAfter = 150m,
            Status = WalletTransactionStatus.Completed, Reference = _reference, Description = "Synthetic funding for test"
        };
        db.Transactions.Add(transaction);
        var intent = await db.PaymentIntents.SingleAsync(p => p.Id == _intentId);
        intent.MarkSucceeded(transaction.Id, "test", "test", _start, _start);
        await db.SaveChangesAsync();
        return Dto(transaction.Id);
    }

    private PaymentVerificationDto Dto(Guid transactionId) => new(
        _intentId, _reference, PaymentStatus.Succeeded, "Succeeded", 50m, "NGN", "test", "test", transactionId, 150m, false);

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

    private static AutomaticPaymentCheckFinalizer Finalizer(ServiceProvider provider, DateTime now) =>
        new(provider.GetRequiredService<IServiceScopeFactory>(), new FixedClock(now), new AutomaticPaymentRetryPolicy());
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
