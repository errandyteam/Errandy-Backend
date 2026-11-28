using Errandy.Application.Features.Errands.AutoReleaseErrand;
using Errandy.Application.Features.Errands.GetErrandsDueForAutoRelease;
using Errandy.Domain.Entities;
using Errandy.Domain.Enums;
using Errandy.Infrastructure.Persistence;
using Errandy.Tests.Support;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Errandy.Tests.Handlers;

public class AutoReleaseTests
{
    // The proof in these tests is submitted at TestData.Now, and the window is 6 hours.
    private static readonly FixedDateTimeProvider SevenHoursLater = new(TestData.Now.AddHours(7));
    private static readonly FixedDateTimeProvider FiveHoursLater = new(TestData.Now.AddHours(5));

    private static AutoReleaseErrandCommandHandler NewHandler(ErrandyDbContext db, RecordingEscrowService escrow, FixedDateTimeProvider clock) =>
        new(db, escrow, clock, NullLogger<AutoReleaseErrandCommandHandler>.Instance);

    [Fact]
    public async Task AfterTheWindow_TheErrandIsCompleted_AndTheRunnerIsPaid()
    {
        var testDb = new TestDatabase();
        var runnerId = Guid.NewGuid();
        var errand = TestData.PendingConfirmationErrand(Guid.NewGuid(), runnerId);
        await testDb.SeedAsync(s => s.Errands.Add(errand));

        var escrow = new RecordingEscrowService();
        using var db = testDb.NewContext();

        await NewHandler(db, escrow, SevenHoursLater).Handle(new AutoReleaseErrandCommand { ErrandId = errand.Id }, CancellationToken.None);

        var saved = await testDb.ReadAsync(r => r.Errands.AsNoTracking().FirstAsync(e => e.Id == errand.Id));
        Assert.Equal(ErrandStatus.Completed, saved.Status);

        var release = Assert.Single(escrow.Calls);
        Assert.Equal("Release", release.Method);
        Assert.Equal(runnerId, release.PartyId);
        Assert.Equal(4800m, release.Amount);
    }

    [Fact]
    public async Task BeforeTheWindowEnds_NothingHappens()
    {
        var testDb = new TestDatabase();
        var errand = TestData.PendingConfirmationErrand(Guid.NewGuid(), Guid.NewGuid());
        await testDb.SeedAsync(s => s.Errands.Add(errand));

        var escrow = new RecordingEscrowService();
        using var db = testDb.NewContext();

        await NewHandler(db, escrow, FiveHoursLater).Handle(new AutoReleaseErrandCommand { ErrandId = errand.Id }, CancellationToken.None);

        var saved = await testDb.ReadAsync(r => r.Errands.AsNoTracking().FirstAsync(e => e.Id == errand.Id));
        Assert.Equal(ErrandStatus.PendingConfirmation, saved.Status);
        Assert.Empty(escrow.Calls);
    }

    [Fact]
    public async Task ADisputedErrand_IsNeverAutoReleased()
    {
        var testDb = new TestDatabase();
        var (errand, dispute) = TestData.DisputedErrand(Guid.NewGuid(), Guid.NewGuid(), withProof: true);
        await testDb.SeedAsync(s => { s.Errands.Add(errand); s.Disputes.Add(dispute); });

        var escrow = new RecordingEscrowService();
        using var db = testDb.NewContext();

        await NewHandler(db, escrow, SevenHoursLater).Handle(new AutoReleaseErrandCommand { ErrandId = errand.Id }, CancellationToken.None);

        var saved = await testDb.ReadAsync(r => r.Errands.AsNoTracking().FirstAsync(e => e.Id == errand.Id));
        Assert.Equal(ErrandStatus.Disputed, saved.Status);
        Assert.Empty(escrow.Calls);
    }

    [Fact]
    public async Task RunningTwice_PaysOnlyOnce()
    {
        var testDb = new TestDatabase();
        var errand = TestData.PendingConfirmationErrand(Guid.NewGuid(), Guid.NewGuid());
        await testDb.SeedAsync(s => s.Errands.Add(errand));

        var escrow = new RecordingEscrowService();
        for (var i = 0; i < 2; i++)
        {
            using var db = testDb.NewContext();
            await NewHandler(db, escrow, SevenHoursLater).Handle(new AutoReleaseErrandCommand { ErrandId = errand.Id }, CancellationToken.None);
        }

        Assert.Single(escrow.Calls);
    }

    [Fact]
    public async Task WhenTheReleaseFails_TheErrandIsStillCompleted_AndNothingCrashes()
    {
        // The handler logs a "RECONCILE:" error for a human to follow up on.
        var testDb = new TestDatabase();
        var errand = TestData.PendingConfirmationErrand(Guid.NewGuid(), Guid.NewGuid());
        await testDb.SeedAsync(s => s.Errands.Add(errand));

        var escrow = new RecordingEscrowService { FailReleases = true };
        using var db = testDb.NewContext();

        await NewHandler(db, escrow, SevenHoursLater).Handle(new AutoReleaseErrandCommand { ErrandId = errand.Id }, CancellationToken.None);

        var saved = await testDb.ReadAsync(r => r.Errands.AsNoTracking().FirstAsync(e => e.Id == errand.Id));
        Assert.Equal(ErrandStatus.Completed, saved.Status);
    }
}

public class DueForAutoReleaseQueryTests
{
    private static Errand PendingSince(DateTime submittedAt)
    {
        var runnerId = Guid.NewGuid();
        var errand = TestData.InProgressErrand(Guid.NewGuid(), runnerId);
        var proof = Proof.Create(errand.Id, "https://example.com/proof.jpg", null, submittedAt);
        errand.AttachProofAndSubmitForConfirmation(runnerId, proof, 4800m, submittedAt);
        return errand;
    }

    [Fact]
    public async Task ReturnsOnlyErrandsWhoseWindowHasPassed()
    {
        var testDb = new TestDatabase();
        var older = PendingSince(TestData.Now.AddHours(-2));
        var newer = PendingSince(TestData.Now);
        await testDb.SeedAsync(s => { s.Errands.Add(older); s.Errands.Add(newer); });

        // Five hours later the cutoff is one hour before Now: only the older errand is past 6 hours.
        using var db = testDb.NewContext();
        var due = await new GetErrandsDueForAutoReleaseQueryHandler(db, new FixedDateTimeProvider(TestData.Now.AddHours(5)))
            .Handle(new GetErrandsDueForAutoReleaseQuery(), CancellationToken.None);

        Assert.Equal(new[] { older.Id }, due);
    }

    [Fact]
    public async Task ReturnsOldestFirst_WhenSeveralAreDue()
    {
        var testDb = new TestDatabase();
        var older = PendingSince(TestData.Now.AddHours(-2));
        var newer = PendingSince(TestData.Now);
        await testDb.SeedAsync(s => { s.Errands.Add(newer); s.Errands.Add(older); });

        using var db = testDb.NewContext();
        var due = await new GetErrandsDueForAutoReleaseQueryHandler(db, new FixedDateTimeProvider(TestData.Now.AddHours(7)))
            .Handle(new GetErrandsDueForAutoReleaseQuery(), CancellationToken.None);

        Assert.Equal(new[] { older.Id, newer.Id }, due);
    }

    [Fact]
    public async Task IgnoresErrandsThatAreNotWaitingOnTheCustomer()
    {
        var testDb = new TestDatabase();
        var inProgress = TestData.InProgressErrand(Guid.NewGuid(), Guid.NewGuid());
        var (disputed, dispute) = TestData.DisputedErrand(Guid.NewGuid(), Guid.NewGuid(), withProof: true);
        await testDb.SeedAsync(s => { s.Errands.Add(inProgress); s.Errands.Add(disputed); s.Disputes.Add(dispute); });

        using var db = testDb.NewContext();
        var due = await new GetErrandsDueForAutoReleaseQueryHandler(db, new FixedDateTimeProvider(TestData.Now.AddHours(30)))
            .Handle(new GetErrandsDueForAutoReleaseQuery(), CancellationToken.None);

        Assert.Empty(due);
    }
}