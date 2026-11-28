using System;
using System.Security.Cryptography;
using System.Threading.Tasks;
using Errandy.Domain.Entities;
using Errandy.Domain.Enums;
using Errandy.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;
using Xunit;
using TransactionEntity = Errandy.Domain.Entities.Transaction;
using WalletTransactionStatus = Errandy.Domain.Enums.TransactionStatus;

namespace Errandy.Tests.Payments;

// Tests a synthetic PendingPayout row within a rolled-back transaction on the
// dedicated local test DB only. No request handler or bank payout is invoked.
public class WithdrawalPendingPayoutPersistencePostgresTests
{
    [Fact]
    public async Task PendingPayout_RoundTripsAsFive_AndWithdrawalUsesXminConcurrency()
    {
        var options = new DbContextOptionsBuilder<ErrandyDbContext>()
            .UseNpgsql(GetSafeTestConnection()).Options;
        await using var db = new ErrandyDbContext(options);

        var type = db.Model.FindEntityType(typeof(WithdrawalRequest));
        Assert.NotNull(type);
        var version = type!.FindProperty("Version");
        Assert.NotNull(version);
        Assert.Equal(typeof(uint), version!.ClrType);
        Assert.True(version.IsConcurrencyToken);
        Assert.Equal(ValueGenerated.OnAddOrUpdate, version.ValueGenerated);
        Assert.Equal("xmin", version.GetColumnName(StoreObjectIdentifier.Table("WithdrawalRequests", null)));

        // All inserts are in this transaction; disposing it rolls them back if
        // an assertion fails, and no synthetic records should be left behind.
        await using var tx = await db.Database.BeginTransactionAsync();
        var userId = Guid.NewGuid();
        var walletId = Guid.NewGuid();
        var transactionId = Guid.NewGuid();
        var phone = RandomNumberGenerator.GetInt32(10_000_000, 100_000_000);
        db.Users.Add(new User
        {
            Id = userId, FullName = "Payout Persistence Test", Email = $"{userId:N}@tests.invalid",
            PhoneNumber = "080" + phone.ToString(), PasswordHash = "test-only-not-a-real-password", Role = UserRole.Customer
        });
        db.Wallets.Add(new Wallet
        {
            Id = walletId, UserId = userId, Balance = 80m, LockedBalance = 0m,
            Currency = "NGN", IsActive = true
        });
        await db.SaveChangesAsync();

        var reference = $"WDR-TEST-{Guid.NewGuid():N}";
        db.Transactions.Add(new TransactionEntity
        {
            Id = transactionId, WalletId = walletId, Type = TransactionType.Debit,
            Category = TransactionCategory.Withdrawal, Amount = 20m,
            BalanceBefore = 100m, BalanceAfter = 80m, Status = WalletTransactionStatus.Pending,
            Reference = reference, Description = "Synthetic pending payout test"
        });
        await db.SaveChangesAsync();
        var request = WithdrawalRequest.CreatePendingPayout(
            userId, walletId, transactionId, 20m, "Test Bank", "0000000000",
            "Test Account", reference, new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc), "000");
        db.WithdrawalRequests.Add(request);
        await db.SaveChangesAsync();

        var id = request.Id;
        db.ChangeTracker.Clear();
        var reloaded = await db.WithdrawalRequests.AsNoTracking().SingleAsync(w => w.Id == id);
        Assert.Equal(WithdrawalStatus.PendingPayout, reloaded.Status);
        Assert.Null(reloaded.ProcessedAt);
        Assert.Equal(transactionId, reloaded.TransactionId);

        // Verify the physical value, not just EF's enum conversion.
        await using var command = new NpgsqlCommand(
            "SELECT \"Status\" FROM \"WithdrawalRequests\" WHERE \"Id\" = @id",
            (NpgsqlConnection)db.Database.GetDbConnection(),
            (NpgsqlTransaction)tx.GetDbTransaction());
        command.Parameters.AddWithValue("id", id);
        var storedStatus = await command.ExecuteScalarAsync();
        Assert.IsType<int>(storedStatus);
        Assert.Equal(5, (int)storedStatus!);

        await tx.RollbackAsync();
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
}
