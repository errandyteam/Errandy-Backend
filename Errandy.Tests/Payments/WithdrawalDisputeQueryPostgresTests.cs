using System;
using System.Linq;
using System.Security.Cryptography;
using System.Threading.Tasks;
using Errandy.Application.Features.Wallets.Queries;
using Errandy.Domain.Entities;
using Errandy.Domain.Enums;
using Errandy.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;
using Xunit;

namespace Errandy.Tests.Payments;

// Synthetic errands/dispute, all created in a rolled-back transaction in the
// dedicated test DB. No withdrawal, escrow, payout, or dispute handler runs.
public class WithdrawalDisputeQueryPostgresTests
{
    [Fact]
    public async Task Query_MatchesCustomerAndRunnerOfDisputedErrand_InPostgres()
    {
        var options = new DbContextOptionsBuilder<ErrandyDbContext>()
            .UseNpgsql(GetSafeTestConnection()).Options;
        await using var db = new ErrandyDbContext(options);
        await using var tx = await db.Database.BeginTransactionAsync();

        // This query compares user IDs; it does not check user/runner roles.
        var ids = Enumerable.Range(0, 5).Select(_ => Guid.NewGuid()).ToArray();
        var phone = RandomNumberGenerator.GetInt32(10_000_000, 99_999_990);
        for (var i = 0; i < ids.Length; i++)
        {
            db.Users.Add(new User
            {
                Id = ids[i], FullName = "Dispute Query Test User", Email = $"{ids[i]:N}@tests.invalid",
                PhoneNumber = "080" + (phone + i).ToString(),
                PasswordHash = "test-only-not-a-real-password", Role = UserRole.Customer
            });
        }
        await db.SaveChangesAsync();

        var now = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var category = Enum.GetValues<ErrandCategory>()[0];
        var reason = Enum.GetValues<DisputeReason>()[0];
        var disputed = NewErrand(ids[0], category, now);
        disputed.Accept(ids[1], now);
        disputed.Start(ids[1], now);
        var dispute = Dispute.Create(disputed.Id, ids[0], reason,
            "Synthetic test dispute", Array.Empty<string>(), now);
        disputed.RaiseDispute(dispute);

        var inProgress = NewErrand(ids[2], category, now);
        inProgress.Accept(ids[3], now);
        inProgress.Start(ids[3], now);
        var cancelled = NewErrand(ids[2], category, now);
        cancelled.Cancel(now);
        db.Errands.AddRange(disputed, inProgress, cancelled);
        await db.SaveChangesAsync();

        var disputedId = disputed.Id;
        var disputeId = dispute.Id;
        db.ChangeTracker.Clear();
        var persistedDispute = await db.Disputes.AsNoTracking().SingleAsync(d => d.Id == disputeId);
        Assert.Equal(disputedId, persistedDispute.ErrandId);
        Assert.Equal(DisputeStatus.Open, persistedDispute.Status);

        Assert.True(await HasDispute(db, ids[0]));  // customer
        Assert.True(await HasDispute(db, ids[1]));  // runner
        Assert.False(await HasDispute(db, ids[2])); // customer of non-disputed errands
        Assert.False(await HasDispute(db, ids[3])); // runner of in-progress errand
        Assert.False(await HasDispute(db, ids[4])); // unrelated user

        // Local configuration maps ErrandStatus as strings; verify the actual
        // stored value in the test database as well as the EF query results.
        await using var command = new NpgsqlCommand(
            "SELECT \"Status\" FROM \"Errands\" WHERE \"Id\" = @id",
            (NpgsqlConnection)db.Database.GetDbConnection(),
            (NpgsqlTransaction)tx.GetDbTransaction());
        command.Parameters.AddWithValue("id", disputedId);
        Assert.Equal("Disputed", await command.ExecuteScalarAsync());

        await tx.RollbackAsync();
    }

    private static Errand NewErrand(Guid customer, ErrandCategory category, DateTime now) =>
        Errand.Create(customer, category, "Synthetic query-only errand", 20m,
            0, 0, ErrandTimePreference.Asap, null, now);

    private static Task<bool> HasDispute(ErrandyDbContext db, Guid userId) =>
        WithdrawalDisputeQuery.ForUser(db.Errands.AsNoTracking(), userId).AnyAsync();

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
