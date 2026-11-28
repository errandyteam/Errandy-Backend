using System;
using System.Linq;
using System.Security.Cryptography;
using System.Threading.Tasks;
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

namespace Errandy.Tests.Payments;

// One explicitly selected payment per call; no background worker or Paystack.
public class AutomaticPaymentCheckProvisionerPostgresTests : IAsyncLifetime
{
    private readonly string _connection = GetSafeTestConnection();
    private readonly Guid _customerId = Guid.NewGuid();
    private readonly Guid _walletId = Guid.NewGuid();
    private readonly string _reference = $"PAY-{Guid.NewGuid():N}";
    private readonly DateTime _createdAt = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
    private Guid _intentId;

    public async Task InitializeAsync()
    {
        await using var db = CreateContext();
        var phone = RandomNumberGenerator.GetInt32(10_000_000, 100_000_000);
        db.Users.Add(new User
        {
            Id = _customerId, FullName = "Provision Test Customer", Email = $"{_customerId:N}@tests.invalid",
            PhoneNumber = "080" + phone.ToString(), PasswordHash = "test-only-not-a-real-password", Role = UserRole.Customer
        });
        db.Wallets.Add(new Wallet
        {
            Id = _walletId, UserId = _customerId, Balance = 100m, LockedBalance = 0m,
            Currency = "NGN", IsActive = true
        });
        var intent = NewIntent(_reference, "Paystack");
        _intentId = intent.Id;
        db.PaymentIntents.Add(intent);
        await db.SaveChangesAsync();
    }

    public async Task DisposeAsync()
    {
        await using var db = CreateContext();
        var ids = await db.PaymentIntents.Where(p => p.UserId == _customerId && p.WalletId == _walletId)
            .Select(p => p.Id).ToListAsync();
        var checks = await db.AutomaticPaymentChecks.Where(c => ids.Contains(c.PaymentIntentId)).ToListAsync();
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
    public async Task PendingPaystackPayment_IsScheduledWithoutChangingMoney()
    {
        using var provider = CreateServices();
        var result = await Provisioner(provider).EnsureScheduledAsync(_intentId);
        Assert.Equal(AutomaticPaymentCheckProvisionResult.Created, result);
        await using var verify = CreateContext();
        var check = await verify.AutomaticPaymentChecks.AsNoTracking().SingleAsync(c => c.PaymentIntentId == _intentId);
        Assert.Equal(AutomaticPaymentCheckStatus.Scheduled, check.Status);
        Assert.Equal((DateTime?)_createdAt.AddMinutes(5), check.NextAttemptAt);
        Assert.Equal(10, check.MaximumAttempts);
        Assert.Equal(0, check.AttemptsStarted);
        Assert.Equal(PaymentStatus.Pending, (await verify.PaymentIntents.AsNoTracking().SingleAsync(p => p.Id == _intentId)).Status);
        Assert.Equal(100m, (await verify.Wallets.AsNoTracking().SingleAsync(w => w.Id == _walletId)).Balance);
        Assert.False(await verify.Transactions.AsNoTracking().AnyAsync(t => t.WalletId == _walletId));
    }

    [Fact]
    public async Task RepeatedProvision_DoesNotCreateOrResetExistingCheck()
    {
        using var provider = CreateServices();
        var provisioner = Provisioner(provider);
        Assert.Equal(AutomaticPaymentCheckProvisionResult.Created, await provisioner.EnsureScheduledAsync(_intentId));
        Assert.Equal(AutomaticPaymentCheckProvisionResult.AlreadyScheduled, await provisioner.EnsureScheduledAsync(_intentId));
        await using var db = CreateContext();
        Assert.Equal(1, await db.AutomaticPaymentChecks.CountAsync(c => c.PaymentIntentId == _intentId));
    }

    [Fact]
    public async Task ExistingNeedsReviewCheck_IsNotReset()
    {
        var due = _createdAt.AddMinutes(5);
        await using (var db = CreateContext())
        {
            var existing = AutomaticPaymentCheck.Schedule(_intentId, due, 1);
            var token = Guid.NewGuid();
            Assert.True(existing.TryStart(token, due, TimeSpan.FromMinutes(2)));
            existing.MarkUnconfirmed(token, due.AddMinutes(1), null);
            db.AutomaticPaymentChecks.Add(existing);
            await db.SaveChangesAsync();
        }
        using var provider = CreateServices();
        Assert.Equal(AutomaticPaymentCheckProvisionResult.AlreadyScheduled, await Provisioner(provider).EnsureScheduledAsync(_intentId));
        await using var verify = CreateContext();
        var check = await verify.AutomaticPaymentChecks.AsNoTracking().SingleAsync(c => c.PaymentIntentId == _intentId);
        Assert.Equal(AutomaticPaymentCheckStatus.NeedsReview, check.Status);
        Assert.Equal(1, check.AttemptsStarted);
        Assert.Null(check.NextAttemptAt);
    }

    [Fact]
    public async Task MissingPayment_IsNotEligible()
    {
        using var provider = CreateServices();
        var result = await Provisioner(provider).EnsureScheduledAsync(Guid.NewGuid());
        Assert.Equal(AutomaticPaymentCheckProvisionResult.NotEligible, result);
        await AssertNoChecksAsync();
    }

    [Fact]
    public async Task CompletedPayment_IsNotEligible()
    {
        await using (var db = CreateContext())
        {
            var intent = await db.PaymentIntents.SingleAsync(p => p.Id == _intentId);
            intent.MarkSucceeded(Guid.NewGuid(), "test", "test", _createdAt, _createdAt);
            await db.SaveChangesAsync();
        }
        using var provider = CreateServices();
        var result = await Provisioner(provider).EnsureScheduledAsync(_intentId);
        Assert.Equal(AutomaticPaymentCheckProvisionResult.NotEligible, result);
        await AssertNoChecksAsync();
    }

    [Fact]
    public async Task DifferentProvider_IsNotEligible()
    {
        Guid otherId;
        await using (var db = CreateContext())
        {
            var other = NewIntent($"OTHER-{Guid.NewGuid():N}", "OtherProvider");
            otherId = other.Id;
            db.PaymentIntents.Add(other);
            await db.SaveChangesAsync();
        }
        using var provider = CreateServices();
        Assert.Equal(AutomaticPaymentCheckProvisionResult.NotEligible, await Provisioner(provider).EnsureScheduledAsync(otherId));
        await using var verify = CreateContext();
        Assert.False(await verify.AutomaticPaymentChecks.AnyAsync(c => c.PaymentIntentId == otherId));
    }

    [Fact]
    public async Task ConcurrentProvisioning_LeavesOneCheck()
    {
        using var provider = CreateServices();
        var one = Provisioner(provider);
        var two = Provisioner(provider);
        var results = await Task.WhenAll(one.EnsureScheduledAsync(_intentId), two.EnsureScheduledAsync(_intentId));
        Assert.Equal(1, results.Count(r => r == AutomaticPaymentCheckProvisionResult.Created));
        Assert.Equal(1, results.Count(r => r == AutomaticPaymentCheckProvisionResult.AlreadyScheduled));
        await using var verify = CreateContext();
        Assert.Equal(1, await verify.AutomaticPaymentChecks.CountAsync(c => c.PaymentIntentId == _intentId));
    }

    [Fact]
    public async Task ProvisioningCommit_IsIndependentOfCallerAmbientTransaction()
    {
        using var provider = CreateServices();
        using (var outer = new System.Transactions.TransactionScope(
            System.Transactions.TransactionScopeOption.Required,
            System.Transactions.TransactionScopeAsyncFlowOption.Enabled))
        {
            Assert.Equal(AutomaticPaymentCheckProvisionResult.Created, await Provisioner(provider).EnsureScheduledAsync(_intentId));
            // Do not complete the caller's transaction.
        }
        await using var verify = CreateContext();
        Assert.True(await verify.AutomaticPaymentChecks.AsNoTracking().AnyAsync(c => c.PaymentIntentId == _intentId));
    }

    [Fact]
    public async Task EmptyId_IsRejected()
    {
        using var provider = CreateServices();
        await Assert.ThrowsAsync<ArgumentException>(() => Provisioner(provider).EnsureScheduledAsync(Guid.Empty));
        await AssertNoChecksAsync();
    }

    private async Task AssertNoChecksAsync()
    {
        await using var db = CreateContext();
        Assert.False(await db.AutomaticPaymentChecks.AnyAsync(c => c.PaymentIntentId == _intentId));
    }

    private PaymentIntent NewIntent(string reference, string provider)
    {
        var intent = PaymentIntent.Create(_customerId, _walletId, 50m, reference, $"{_customerId:N}@tests.invalid", provider);
        intent.CreatedAt = _createdAt;
        return intent;
    }

    private ServiceProvider CreateServices()
    {
        var services = new ServiceCollection();
        services.AddDbContext<ErrandyDbContext>(options => options.UseNpgsql(_connection));
        services.AddScoped<IApplicationDbContext>(sp => sp.GetRequiredService<ErrandyDbContext>());
        return services.BuildServiceProvider(validateScopes: true);
    }

    private static AutomaticPaymentCheckProvisioner Provisioner(ServiceProvider provider) =>
        new(provider.GetRequiredService<IServiceScopeFactory>(), new AutomaticPaymentRetryPolicy());
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
