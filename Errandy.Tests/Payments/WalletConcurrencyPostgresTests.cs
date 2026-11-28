using System;
using System.Linq;
using System.Security.Cryptography;
using System.Threading.Tasks;
using Errandy.Domain.Entities;
using Errandy.Domain.Enums;
using Errandy.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Xunit;
using TransactionEntity = Errandy.Domain.Entities.Transaction;
using WalletTransactionStatus = Errandy.Domain.Enums.TransactionStatus;

namespace Errandy.Tests.Payments;

public class WalletConcurrencyPostgresTests
{
    [Fact]
    public async Task StaleWalletUpdate_RollsBackItsLedgerEntry()
    {
        var connectionString = GetSafeTestConnection();
        var userId = Guid.NewGuid();
        var walletId = Guid.NewGuid();
        var reference = $"TEST-{Guid.NewGuid():N}";
        var phone = "080" + RandomNumberGenerator.GetInt32(10_000_000, 100_000_000).ToString();

        try
        {
            // These records exist only for this test and are cleaned up below.
            await using (var seed = CreateContext(connectionString))
            {
                seed.Users.Add(new User
                {
                    Id = userId,
                    FullName = "Payment Test User",
                    Email = $"{userId:N}@tests.invalid",
                    PhoneNumber = phone,
                    PasswordHash = "test-only-not-a-real-password",
                    Role = UserRole.Customer
                });
                seed.Wallets.Add(new Wallet
                {
                    Id = walletId,
                    UserId = userId,
                    Balance = 100m,
                    LockedBalance = 0m,
                    Currency = "NGN",
                    IsActive = true
                });
                await seed.SaveChangesAsync();
            }

            await using var first = CreateContext(connectionString);
            await using var second = CreateContext(connectionString);

            // Both contexts read the same original row before either updates it.
            var firstWallet = await first.Wallets.SingleAsync(w => w.Id == walletId);
            var staleWallet = await second.Wallets.SingleAsync(w => w.Id == walletId);
            Assert.Equal(100m, firstWallet.Balance);
            Assert.Equal(100m, staleWallet.Balance);

            firstWallet.Credit(20m);
            await first.SaveChangesAsync();

            // Simulate a competing financial operation that also adds a ledger row.
            staleWallet.Credit(30m);
            second.Transactions.Add(new TransactionEntity
            {
                WalletId = walletId,
                Type = TransactionType.Credit,
                Category = TransactionCategory.Funding,
                Amount = 30m,
                BalanceBefore = 100m,
                BalanceAfter = 130m,
                Status = WalletTransactionStatus.Completed,
                Reference = reference,
                Description = "Concurrency test only"
            });

            await Assert.ThrowsAsync<DbUpdateConcurrencyException>(
                async () => await second.SaveChangesAsync());

            await using var check = CreateContext(connectionString);
            var current = await check.Wallets.AsNoTracking().SingleAsync(w => w.Id == walletId);
            Assert.Equal(120m, current.Balance);
            Assert.False(await check.Transactions.AsNoTracking().AnyAsync(t => t.Reference == reference));
        }
        finally
        {
            // Delete only the rows identified by this test's randomly generated IDs.
            await using var cleanup = CreateContext(connectionString);
            var transactions = await cleanup.Transactions.Where(t => t.WalletId == walletId).ToListAsync();
            cleanup.Transactions.RemoveRange(transactions);
            await cleanup.SaveChangesAsync();

            var wallet = await cleanup.Wallets.SingleOrDefaultAsync(w => w.Id == walletId && w.UserId == userId);
            if (wallet is not null)
            {
                cleanup.Wallets.Remove(wallet);
                await cleanup.SaveChangesAsync();
            }

            var user = await cleanup.Users.SingleOrDefaultAsync(u => u.Id == userId);
            if (user is not null)
            {
                cleanup.Users.Remove(user);
                await cleanup.SaveChangesAsync();
            }
        }
    }

    private static ErrandyDbContext CreateContext(string connectionString)
    {
        var options = new DbContextOptionsBuilder<ErrandyDbContext>()
            .UseNpgsql(connectionString)
            .Options;
        return new ErrandyDbContext(options);
    }

    private static string GetSafeTestConnection()
    {
        var connectionString = Environment.GetEnvironmentVariable("ERRANDY_PAYMENT_TEST_CONNECTION");
        if (string.IsNullOrWhiteSpace(connectionString))
            throw new InvalidOperationException("Set ERRANDY_PAYMENT_TEST_CONNECTION for the dedicated local test database.");

        NpgsqlConnectionStringBuilder parsed;
        try
        {
            parsed = new NpgsqlConnectionStringBuilder(connectionString);
        }
        catch (ArgumentException)
        {
            // Do not include the connection string in the test error.
            throw new InvalidOperationException("The test connection string is invalid.");
        }

        if (!string.Equals(parsed.Host, "localhost", StringComparison.OrdinalIgnoreCase) ||
            parsed.Port != 5432 ||
            !string.Equals(parsed.Database, "errandy_payment_tests", StringComparison.Ordinal))
            throw new InvalidOperationException("Tests are restricted to localhost:5432/errandy_payment_tests.");

        return connectionString;
    }
}
