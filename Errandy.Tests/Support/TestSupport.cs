using Errandy.Application.Interfaces;
using Errandy.Domain.Entities;
using Errandy.Domain.Enums;
using Errandy.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Errandy.Tests.Support;

/// <summary>A clock that always shows the same time, so tests never depend on "now".</summary>
public sealed class FixedDateTimeProvider : IDateTimeProvider
{
    public FixedDateTimeProvider(DateTime utcNow) => UtcNow = utcNow;
    public DateTime UtcNow { get; }
}

public sealed record EscrowCall(string Method, Guid ErrandId, Guid PartyId, decimal Amount);

/// <summary>Stands in for Engineer B's escrow. Records every call so tests can check what money would have moved.</summary>
public sealed class RecordingEscrowService : IEscrowService
{
    public List<EscrowCall> Calls { get; } = new();

    /// <summary>When true, refunds throw, to simulate the money system failing.</summary>
    public bool FailRefunds { get; set; }
    /// <summary>When true, releases throw, to simulate the money system failing.</summary>
    public bool FailReleases { get; set; }
    /// <summary>When true, price adjustments throw, to simulate "not enough balance".</summary>
    public bool FailAdjustments { get; set; }
    public Task LockFundsAsync(Guid errandId, Guid customerId, decimal amount, CancellationToken cancellationToken = default)
    {
        Calls.Add(new EscrowCall("Lock", errandId, customerId, amount));
        return Task.CompletedTask;
    }

    public Task ReleaseFundsAsync(Guid errandId, Guid runnerId, decimal finalAmount, CancellationToken cancellationToken = default)
    {
        if (FailReleases)
            throw new InvalidOperationException("Simulated release failure.");

        Calls.Add(new EscrowCall("Release", errandId, runnerId, finalAmount));
        return Task.CompletedTask;
    }

    public Task RefundFundsAsync(Guid errandId, Guid customerId, decimal amount, CancellationToken cancellationToken = default)
    {
        if (FailRefunds)
            throw new InvalidOperationException("Simulated refund failure.");

        Calls.Add(new EscrowCall("Refund", errandId, customerId, amount));
        return Task.CompletedTask;
    }

    public Task PartialRefundAsync(Guid errandId, Guid customerId, decimal refundAmount, decimal releaseToRunnerAmount, CancellationToken cancellationToken = default)
    {
        Calls.Add(new EscrowCall("PartialRefund", errandId, customerId, refundAmount));
        return Task.CompletedTask;
    }

    public Task AdjustLockedFundsAsync(Guid errandId, Guid customerId, decimal oldAmount, decimal newAmount, CancellationToken cancellationToken = default)
    {
        if (FailAdjustments)
            throw new InvalidOperationException("Simulated adjustment failure.");

        Calls.Add(new EscrowCall("Adjust", errandId, customerId, newAmount));
        return Task.CompletedTask;
    }
}



/// <summary>
/// A fresh in-memory copy of the real database model for each test. Seeding, running the
/// handler and checking the result each use their own context, the same way separate web
/// requests would, so a test can't pass just because something was still held in memory.
/// </summary>
public sealed class TestDatabase
{
    private readonly DbContextOptions<ErrandyDbContext> _options =
        new DbContextOptionsBuilder<ErrandyDbContext>()
            .UseInMemoryDatabase("errandy-tests-" + Guid.NewGuid())
            .Options;

    public ErrandyDbContext NewContext() => new(_options);

    public async Task SeedAsync(Action<ErrandyDbContext> seed)
    {
        using var db = NewContext();
        seed(db);
        await db.SaveChangesAsync();
    }

    public async Task<T> ReadAsync<T>(Func<ErrandyDbContext, Task<T>> read)
    {
        using var db = NewContext();
        return await read(db);
    }
}

public static class TestData
{
    public static readonly DateTime Now = new(2026, 9, 25, 12, 0, 0, DateTimeKind.Utc);
    public static readonly FixedDateTimeProvider Clock = new(Now);

    /// <summary>An ASAP errand, so its deadline is createdAt + 2 hours.</summary>
    public static Errand NewErrand(Guid? customerId = null, decimal cost = 5000m, DateTime? createdAt = null) =>
        Errand.Create(customerId ?? Guid.NewGuid(), ErrandCategory.MarketGrocery, "Buy rice and beans",
            cost, 6.6018, 3.3515, ErrandTimePreference.Asap, null, createdAt ?? Now);

    public static RunnerProfile Runner(Guid userId, KycStatus status) =>
        new() { UserId = userId, KycStatus = status };

    public static Errand AcceptedErrand(Guid customerId, Guid runnerId)
    {
        var errand = NewErrand(customerId);
        errand.Accept(runnerId, Now);
        return errand;
    }

    public static Errand InProgressErrand(Guid customerId, Guid runnerId)
    {
        var errand = AcceptedErrand(customerId, runnerId);
        errand.Start(runnerId, Now);
        return errand;
    }

    public static Errand PendingConfirmationErrand(Guid customerId, Guid runnerId)
    {
        var errand = InProgressErrand(customerId, runnerId);
        var proof = Proof.Create(errand.Id, "https://example.com/proof.jpg", null, Now);
        errand.AttachProofAndSubmitForConfirmation(runnerId, proof, 4800m, Now);
        return errand;
    }

    public static (Errand Errand, Dispute Dispute) DisputedErrand(Guid customerId, Guid runnerId, bool withProof)
    {
        var errand = withProof
            ? PendingConfirmationErrand(customerId, runnerId)
            : InProgressErrand(customerId, runnerId);

        var dispute = Dispute.Create(errand.Id, customerId, DisputeReason.PoorService,
            "Not what I asked for", new[] { "https://example.com/evidence1.jpg" }, Now);

        errand.RaiseDispute(dispute);
        return (errand, dispute);
    }
}