using System;
using System.Data.Common;
using System.Linq;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using Errandy.Application.Common.Interfaces;
using Errandy.Application.Features.Wallets.Commands.CancelWithdrawal;
using Errandy.Application.Features.Wallets.Commands.ProcessWithdrawal;
using Errandy.Application.Features.Wallets.Commands.RequestWithdrawal;
using Errandy.Application.Interfaces;
using Errandy.Domain.Entities;
using Errandy.Domain.Enums;
using Errandy.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Configuration;
using Npgsql;
using Xunit;
using WalletTransactionStatus = Errandy.Domain.Enums.TransactionStatus;

namespace Errandy.Tests.Payments;

// Synthetic records only. This test must use the dedicated local test DB.
// No bank transfer is initiated. It deliberately exercises a refund race.
public class WithdrawalCancellationRacePostgresTests : IAsyncLifetime
{
    private readonly string _connection = GetSafeTestConnection();
    private readonly Guid _userId = Guid.NewGuid();
    private readonly Guid _adminId = Guid.NewGuid();
    private readonly Guid _walletId = Guid.NewGuid();
    private readonly FixedClock _clock = new(new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc));
    private Guid _withdrawalId;
    private Guid _originalTransactionId;

    public async Task InitializeAsync()
    {
        await using var db = CreateContext();
        var phone = RandomNumberGenerator.GetInt32(10_000_000, 99_999_998);
        db.Users.Add(new User
        {
            Id = _userId,
            FullName = "Cancellation Race Customer",
            Email = $"{_userId:N}@tests.invalid",
            PhoneNumber = "080" + phone.ToString(),
            PasswordHash = "test-only-not-a-real-password",
            Role = UserRole.Customer
        });
        db.Users.Add(new User
        {
            Id = _adminId,
            FullName = "Cancellation Race Admin",
            Email = $"{_adminId:N}@tests.invalid",
            PhoneNumber = "080" + (phone + 1).ToString(),
            PasswordHash = "test-only-not-a-real-password",
            Role = UserRole.Admin
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
        var handler = new RequestWithdrawalCommandHandler(db, Customer(), _clock, new ConfigurationBuilder().Build());
        var result = await handler.Handle(
            new RequestWithdrawalCommand(20m, "Test Bank", "0000000000", "Test Account", "000"),
            CancellationToken.None);
        Assert.True(result.IsSuccess, result.Error);
        var withdrawal = await db.WithdrawalRequests.AsNoTracking().SingleAsync(w => w.UserId == _userId);
        Assert.Equal(WithdrawalStatus.PendingHold, withdrawal.Status);
        _withdrawalId = withdrawal.Id;
        _originalTransactionId = withdrawal.TransactionId;
    }

    public async Task DisposeAsync()
    {
        await using var db = CreateContext();
        db.WithdrawalRequests.RemoveRange(await db.WithdrawalRequests.Where(w => w.UserId == _userId && w.WalletId == _walletId).ToListAsync());
        await db.SaveChangesAsync();
        db.Transactions.RemoveRange(await db.Transactions.Where(t => t.WalletId == _walletId).ToListAsync());
        await db.SaveChangesAsync();
        var wallet = await db.Wallets.SingleOrDefaultAsync(w => w.Id == _walletId && w.UserId == _userId);
        if (wallet is not null) { db.Wallets.Remove(wallet); await db.SaveChangesAsync(); }
        var users = await db.Users.Where(u => u.Id == _userId || u.Id == _adminId).ToListAsync();
        db.Users.RemoveRange(users);
        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task Cancellation_StaleWithdrawalAndFreshWallet_MustNotRefundTwice()
    {
        var pause = new PauseBeforeWalletRead();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(25));
        await using var secondDb = CreateContext(pause);
        var secondHandler = new CancelWithdrawalCommandHandler(secondDb, Customer(), _clock);

        // B reads PendingHold, then pauses before reading the wallet. A completes
        // the cancellation; only then does B read the newly refunded wallet.
        var secondTask = secondHandler.Handle(new CancelWithdrawalCommand(_withdrawalId), timeout.Token);
        try
        {
            await pause.Reached.Task.WaitAsync(timeout.Token);
            await using var firstDb = CreateContext();
            var first = await new CancelWithdrawalCommandHandler(firstDb, Customer(), _clock)
                .Handle(new CancelWithdrawalCommand(_withdrawalId), timeout.Token);
            Assert.True(first.IsSuccess, first.Error);
        }
        finally
        {
            pause.Continue.TrySetResult(true);
        }

        bool secondSucceeded;
        try
        {
            secondSucceeded = (await secondTask).IsSuccess;
        }
        catch (DbUpdateConcurrencyException)
        {
            // A future concurrency-token fix may reject B at SaveChanges.
            secondSucceeded = false;
        }

        await using var verify = CreateContext();
        var wallet = await verify.Wallets.AsNoTracking().SingleAsync(w => w.Id == _walletId);
        var withdrawal = await verify.WithdrawalRequests.AsNoTracking().SingleAsync(w => w.Id == _withdrawalId);
        var original = await verify.Transactions.AsNoTracking().SingleAsync(t => t.Id == _originalTransactionId);
        var credits = await verify.Transactions.AsNoTracking().Where(t =>
            t.WalletId == _walletId && t.Category == TransactionCategory.Withdrawal && t.Type == TransactionType.Credit).ToListAsync();

        Assert.Equal(100m, wallet.Balance);
        Assert.Equal(WithdrawalStatus.Cancelled, withdrawal.Status);
        Assert.Equal(WalletTransactionStatus.Reversed, original.Status);
        var credit = Assert.Single(credits);
        Assert.Equal(20m, credit.Amount);
        Assert.False(secondSucceeded, "The stale cancellation must not succeed a second time.");
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task CancellationAndRejection_WhenOneReadIsStale_MustRefundOnlyOnce(bool pauseRejection)
    {
        var pause = new PauseBeforeWalletRead();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(25));
        await using var secondDb = CreateContext(pause);

        // The loser reads PendingHold, then waits before fetching the wallet.
        var secondTask = pauseRejection
            ? new ProcessWithdrawalCommandHandler(secondDb, Admin(), _clock)
                .Handle(new ProcessWithdrawalCommand(_withdrawalId, false, "Synthetic test rejection", false), timeout.Token)
            : new CancelWithdrawalCommandHandler(secondDb, Customer(), _clock)
                .Handle(new CancelWithdrawalCommand(_withdrawalId), timeout.Token);
        try
        {
            await pause.Reached.Task.WaitAsync(timeout.Token);
            await using var firstDb = CreateContext();
            var first = pauseRejection
                ? await new CancelWithdrawalCommandHandler(firstDb, Customer(), _clock)
                    .Handle(new CancelWithdrawalCommand(_withdrawalId), timeout.Token)
                : await new ProcessWithdrawalCommandHandler(firstDb, Admin(), _clock)
                    .Handle(new ProcessWithdrawalCommand(_withdrawalId, false, "Synthetic test rejection", false), timeout.Token);
            Assert.True(first.IsSuccess, first.Error);
        }
        finally
        {
            pause.Continue.TrySetResult(true);
        }

        bool secondSucceeded;
        try { secondSucceeded = (await secondTask).IsSuccess; }
        catch (DbUpdateConcurrencyException) { secondSucceeded = false; }

        await using var verify = CreateContext();
        var wallet = await verify.Wallets.AsNoTracking().SingleAsync(w => w.Id == _walletId);
        var withdrawal = await verify.WithdrawalRequests.AsNoTracking().SingleAsync(w => w.Id == _withdrawalId);
        var original = await verify.Transactions.AsNoTracking().SingleAsync(t => t.Id == _originalTransactionId);
        var credits = await verify.Transactions.AsNoTracking().Where(t =>
            t.WalletId == _walletId && t.Category == TransactionCategory.Withdrawal && t.Type == TransactionType.Credit).ToListAsync();
        Assert.Equal(100m, wallet.Balance);
        Assert.Equal(pauseRejection ? WithdrawalStatus.Cancelled : WithdrawalStatus.Rejected, withdrawal.Status);
        Assert.Equal(WalletTransactionStatus.Reversed, original.Status);
        var credit = Assert.Single(credits);
        Assert.Equal(20m, credit.Amount);
        Assert.False(secondSucceeded, "The stale refund operation must not succeed.");
    }

    private FakeCurrentUser Customer() => new(_userId, UserRole.Customer);
    private FakeCurrentUser Admin() => new(_adminId, UserRole.Admin);
    private ErrandyDbContext CreateContext(DbCommandInterceptor? interceptor = null)
    {
        var builder = new DbContextOptionsBuilder<ErrandyDbContext>().UseNpgsql(_connection);
        if (interceptor is not null) builder.AddInterceptors(interceptor);
        return new ErrandyDbContext(builder.Options);
    }

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

    private sealed class PauseBeforeWalletRead : DbCommandInterceptor
    {
        private int _paused;
        public TaskCompletionSource<bool> Reached { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource<bool> Continue { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public override async ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result,
            CancellationToken cancellationToken = default)
        {
            if (command.CommandText.Contains("\"Wallets\"", StringComparison.Ordinal) &&
                command.CommandText.TrimStart().StartsWith("SELECT", StringComparison.OrdinalIgnoreCase) &&
                Interlocked.Exchange(ref _paused, 1) == 0)
            {
                Reached.TrySetResult(true);
                await Continue.Task.WaitAsync(cancellationToken);
            }
            return result;
        }
    }

    private sealed class FixedClock : IDateTimeProvider
    {
        public FixedClock(DateTime utcNow) => UtcNow = utcNow;
        public DateTime UtcNow { get; }
    }
    private sealed class FakeCurrentUser : ICurrentUser
    {
        public FakeCurrentUser(Guid id, UserRole role) { UserId = id; Role = role; }
        public Guid? UserId { get; }
        public string? Email => null;
        public UserRole? Role { get; }
        public bool IsAuthenticated => true;
    }
}
