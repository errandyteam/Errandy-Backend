using System;
using System.Linq;
using Errandy.Application.Features.Wallets.Queries;
using Errandy.Domain.Entities;
using Errandy.Domain.Enums;
using Errandy.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Errandy.Tests.Payments;

// Query-only tests: these do not touch a database or test dispute transitions.
// In-memory fixtures set Status by reflection solely to supply query inputs;
// they do not represent persisted domain-valid dispute records.
public class WithdrawalDisputeQueryTests
{
    [Theory]
    [InlineData("customer-disputed", true)]
    [InlineData("runner-disputed", true)]
    [InlineData("unrelated-disputed", false)]
    [InlineData("customer-completed", false)]
    [InlineData("runner-in-progress", false)]
    [InlineData("null-runner-disputed", false)]
    [InlineData("no-errands", false)]
    public void Query_MatchesOnlyDisputedErrandsInvolvingUser(string scenario, bool expected)
    {
        var target = Guid.NewGuid();
        var otherCustomer = Guid.NewGuid();
        var otherRunner = Guid.NewGuid();
        Errand[] errands = scenario switch
        {
            "customer-disputed" => new[] { Fixture(target, otherRunner, ErrandStatus.Disputed) },
            "runner-disputed" => new[] { Fixture(otherCustomer, target, ErrandStatus.Disputed) },
            "unrelated-disputed" => new[] { Fixture(otherCustomer, otherRunner, ErrandStatus.Disputed) },
            "customer-completed" => new[] { Fixture(target, otherRunner, ErrandStatus.Completed) },
            "runner-in-progress" => new[] { Fixture(otherCustomer, target, ErrandStatus.InProgress) },
            "null-runner-disputed" => new[] { Fixture(otherCustomer, null, ErrandStatus.Disputed) },
            "no-errands" => Array.Empty<Errand>(),
            _ => throw new ArgumentOutOfRangeException(nameof(scenario))
        };
        Assert.Equal(expected, WithdrawalDisputeQuery.ForUser(errands.AsQueryable(), target).Any());
    }

    [Fact]
    public void Query_TranslatesToPostgresSqlWithoutOpeningConnection()
    {
        var options = new DbContextOptionsBuilder<ErrandyDbContext>()
            .UseNpgsql("Host=localhost;Database=query_translation_only").Options;
        using var db = new ErrandyDbContext(options);
        var sql = WithdrawalDisputeQuery.ForUser(db.Errands.AsNoTracking(), Guid.NewGuid()).ToQueryString();
        Assert.Contains("\"Status\"", sql);
        Assert.Contains("\"CustomerId\"", sql);
        Assert.Contains("\"RunnerId\"", sql);
        Assert.Contains(" OR ", sql.ToUpperInvariant());
    }

    private static Errand Fixture(Guid customer, Guid? runner, ErrandStatus status)
    {
        var now = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var errand = Errand.Create(customer, default, "Query-only fixture", 1m, 0, 0,
            ErrandTimePreference.Asap, null, now);
        if (runner.HasValue) errand.Accept(runner.Value, now);
        typeof(Errand).GetProperty(nameof(Errand.Status))!.SetValue(errand, status);
        return errand;
    }
}
