using System;
using System.Linq;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using Errandy.Api.BackgroundServices;
using Errandy.Application.Common.Interfaces;
using Errandy.Application.Common.Results;
using Errandy.Application.Features.Wallets.Commands.ProcessMaturedWithdrawals;
using Errandy.Application.Features.Wallets.Commands.ProcessWithdrawal;
using Errandy.Application.Features.Wallets.Commands.RequestWithdrawal;
using Errandy.Application.Interfaces;
using Errandy.Domain.Entities;
using Errandy.Domain.Enums;
using Errandy.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using Xunit;
using WalletTransactionStatus = Errandy.Domain.Enums.TransactionStatus;

namespace Errandy.Tests.Payments;

// Tests the temporary completion blocks against the dedicated test database.
// The held withdrawal is a synthetic legacy-style fixture; no payout is sent.
public class WithdrawalCompletionSafetyPostgresTests : IAsyncLifetime
{
    private readonly string _connection = GetSafeTestConnection();
    private readonly Guid _adminId = Guid.NewGuid();
    private readonly Guid _customerId = Guid.NewGuid();
    private readonly Guid _walletId = Guid.NewGuid();
    private readonly FixedClock _clock = new(new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc));
    private Guid _withdrawalId;
    private Guid _transactionId;

    public async Task InitializeAsync()
    {
        await using var db = CreateContext();
        var phone = RandomNumberGenerator.GetInt32(10_000_000, 99_999_998);
        db.Users.Add(new User
        {
            Id = _adminId, FullName = "Withdrawal Guard Admin", Email = $"{_adminId:N}@tests.invalid",
            PhoneNumber = "080" + phone.ToString(), PasswordHash = "test-only-not-a-real-password", Role = UserRole.Admin
        });
        db.Users.Add(new User
        {
            Id = _customerId, FullName = "Withdrawal Guard Customer", Email = $"{_customerId:N}@tests.invalid",
            PhoneNumber = "080" + (phone + 1).ToString(), PasswordHash = "test-only-not-a-real-password", Role = UserRole.Customer
        });
        db.Wallets.Add(new Wallet
        {
            Id = _walletId, UserId = _customerId, Balance = 100m, LockedBalance = 0m,
            Currency = "NGN", IsActive = true
        });
        await db.SaveChangesAsync();

        var handler = new RequestWithdrawalCommandHandler(db, Customer(), _clock, new ConfigurationBuilder().Build());
        var created = await handler.Handle(new RequestWithdrawalCommand(20m, "Test Bank", "0000000000", "Test Account", "000"), CancellationToken.None);
        Assert.True(created.IsSuccess, created.Error);
        var withdrawal = await db.WithdrawalRequests.AsNoTracking().SingleAsync(w => w.UserId == _customerId && w.WalletId == _walletId);
        Assert.Equal(WithdrawalStatus.PendingHold, withdrawal.Status);
        _withdrawalId = withdrawal.Id;
        _transactionId = withdrawal.TransactionId;
        // Make the synthetic legacy hold mature, without waiting.
        _clock.UtcNow = _clock.UtcNow.AddDays(2);
    }

    public async Task DisposeAsync()
    {
        await using var db = CreateContext();
        var withdrawals = await db.WithdrawalRequests.Where(w => w.UserId == _customerId && w.WalletId == _walletId).ToListAsync();
        db.WithdrawalRequests.RemoveRange(withdrawals);
        await db.SaveChangesAsync();
        var transactions = await db.Transactions.Where(t => t.WalletId == _walletId).ToListAsync();
        db.Transactions.RemoveRange(transactions);
        await db.SaveChangesAsync();
        var wallet = await db.Wallets.SingleOrDefaultAsync(w => w.Id == _walletId && w.UserId == _customerId);
        if (wallet is not null) { db.Wallets.Remove(wallet); await db.SaveChangesAsync(); }
        var users = await db.Users.Where(u => u.Id == _adminId || u.Id == _customerId).ToListAsync();
        db.Users.RemoveRange(users);
        await db.SaveChangesAsync();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task AdminApproval_IsBlocked_EvenWhenMaturedOrForced(bool forceBypass)
    {
        await using var db = CreateContext();
        var handler = new ProcessWithdrawalCommandHandler(db, Admin(), _clock);
        var result = await handler.Handle(new ProcessWithdrawalCommand(_withdrawalId, true, "test review", forceBypass), CancellationToken.None);
        Assert.False(result.IsSuccess);
        Assert.Equal(ErrorType.Conflict, result.ErrorType);
        await AssertUnchangedAsync();
    }

    [Fact]
    public async Task AdminBatchCompletion_IsBlocked_EvenWhenWithdrawalMatured()
    {
        await using var db = CreateContext();
        var handler = new ProcessMaturedWithdrawalsCommandHandler(db, Admin(), _clock);
        var result = await handler.Handle(new ProcessMaturedWithdrawalsCommand(), CancellationToken.None);
        Assert.False(result.IsSuccess);
        Assert.Equal(ErrorType.Conflict, result.ErrorType);
        await AssertUnchangedAsync();
    }

    [Fact]
    public async Task CustomerWithAdminClaim_CannotUseEitherCompletionCommand()
    {
        await using var db = CreateContext();
        var customerClaimingAdmin = new FakeCurrentUser(_customerId, true, UserRole.Admin);
        var single = await new ProcessWithdrawalCommandHandler(db, customerClaimingAdmin, _clock)
            .Handle(new ProcessWithdrawalCommand(_withdrawalId, true, "test", true), CancellationToken.None);
        var batch = await new ProcessMaturedWithdrawalsCommandHandler(db, customerClaimingAdmin, _clock)
            .Handle(new ProcessMaturedWithdrawalsCommand(), CancellationToken.None);
        Assert.False(single.IsSuccess); Assert.Equal(ErrorType.Forbidden, single.ErrorType);
        Assert.False(batch.IsSuccess); Assert.Equal(ErrorType.Forbidden, batch.ErrorType);
        await AssertUnchangedAsync();
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task UnauthenticatedCaller_IsRejected(bool batch)
    {
        await using var db = CreateContext();
        var user = new FakeCurrentUser(null, false, null);
        if (batch)
        {
            var result = await new ProcessMaturedWithdrawalsCommandHandler(db, user, _clock)
                .Handle(new ProcessMaturedWithdrawalsCommand(), CancellationToken.None);
            Assert.False(result.IsSuccess); Assert.Equal(ErrorType.Unauthorized, result.ErrorType);
        }
        else
        {
            var result = await new ProcessWithdrawalCommandHandler(db, user, _clock)
                .Handle(new ProcessWithdrawalCommand(_withdrawalId, true, "test", true), CancellationToken.None);
            Assert.False(result.IsSuccess); Assert.Equal(ErrorType.Unauthorized, result.ErrorType);
        }
        await AssertUnchangedAsync();
    }

    [Fact]
    public async Task BackgroundService_StartsAndStopsWithoutCreatingDatabaseScope()
    {
        var scopes = new ThrowingScopeFactory();
        using var service = new WithdrawalHoldBackgroundService(scopes, NullLogger<WithdrawalHoldBackgroundService>.Instance);
        await service.StartAsync(CancellationToken.None);
        Assert.NotNull(service.ExecuteTask);
        Assert.True(service.ExecuteTask!.IsCompletedSuccessfully);
        await service.StopAsync(CancellationToken.None);
        Assert.Equal(0, scopes.Calls);
        await AssertUnchangedAsync();
    }

    private async Task AssertUnchangedAsync()
    {
        await using var db = CreateContext();
        var withdrawal = await db.WithdrawalRequests.AsNoTracking().SingleAsync(w => w.Id == _withdrawalId);
        var transaction = await db.Transactions.AsNoTracking().SingleAsync(t => t.Id == _transactionId);
        var wallet = await db.Wallets.AsNoTracking().SingleAsync(w => w.Id == _walletId);
        Assert.Equal(WithdrawalStatus.PendingHold, withdrawal.Status);
        Assert.Equal(WalletTransactionStatus.Pending, transaction.Status);
        Assert.Equal(80m, wallet.Balance);
        Assert.Equal(1, await db.Transactions.AsNoTracking().CountAsync(t => t.WalletId == _walletId));
    }

    private FakeCurrentUser Admin() => new(_adminId, true, UserRole.Admin);
    private FakeCurrentUser Customer() => new(_customerId, true, UserRole.Customer);
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

    private sealed class FixedClock : IDateTimeProvider
    {
        public FixedClock(DateTime now) => UtcNow = now;
        public DateTime UtcNow { get; set; }
    }

    private sealed class FakeCurrentUser : ICurrentUser
    {
        public FakeCurrentUser(Guid? id, bool authenticated, UserRole? role) { UserId = id; IsAuthenticated = authenticated; Role = role; }
        public Guid? UserId { get; }
        public string? Email => null;
        public UserRole? Role { get; }
        public bool IsAuthenticated { get; }
    }

    private sealed class ThrowingScopeFactory : IServiceScopeFactory
    {
        public int Calls { get; private set; }
        public IServiceScope CreateScope()
        {
            Calls++;
            throw new InvalidOperationException("The disabled service must not create a database scope.");
        }
    }
}
