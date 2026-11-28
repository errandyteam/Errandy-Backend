using Errandy.Application.Common.Exceptions;
using Errandy.Application.Features.Errands.CancelErrand;
using Errandy.Application.Features.Errands.ExpireUnacceptedErrands;
using Errandy.Domain.Enums;
using Errandy.Tests.Support;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Errandy.Tests.Handlers;

public class CancelErrandTests
{
    [Fact]
    public async Task Customer_CancellingBeforeAcceptance_GetsAFullRefund()
    {
        var testDb = new TestDatabase();
        var customerId = Guid.NewGuid();
        var errand = TestData.NewErrand(customerId, cost: 5000m);
        await testDb.SeedAsync(s => s.Errands.Add(errand));

        var escrow = new RecordingEscrowService();
        using var db = testDb.NewContext();
        var handler = new CancelErrandCommandHandler(db, escrow, TestData.Clock, NullLogger<CancelErrandCommandHandler>.Instance);

        await handler.Handle(new CancelErrandCommand { ErrandId = errand.Id, RequestingUserId = customerId }, CancellationToken.None);

        var saved = await testDb.ReadAsync(r => r.Errands.AsNoTracking().FirstAsync(e => e.Id == errand.Id));
        Assert.Equal(ErrandStatus.Cancelled, saved.Status);

        var refund = Assert.Single(escrow.Calls);
        Assert.Equal("Refund", refund.Method);
        Assert.Equal(customerId, refund.PartyId);
        Assert.Equal(5000m, refund.Amount);
    }

    [Fact]
    public async Task Customer_CancellingAfterAcceptance_LosesThePenaltyShare()
    {
        // The penalty is a placeholder 20% (PRD says 10-30%). When the team agrees
        // the real number, update the 4000m below to match.
        var testDb = new TestDatabase();
        var customerId = Guid.NewGuid();
        var runnerId = Guid.NewGuid();
        var errand = TestData.AcceptedErrand(customerId, runnerId);
        await testDb.SeedAsync(s => s.Errands.Add(errand));

        var escrow = new RecordingEscrowService();
        using var db = testDb.NewContext();
        var handler = new CancelErrandCommandHandler(db, escrow, TestData.Clock, NullLogger<CancelErrandCommandHandler>.Instance);

        await handler.Handle(new CancelErrandCommand { ErrandId = errand.Id, RequestingUserId = customerId }, CancellationToken.None);

        var refund = Assert.Single(escrow.Calls);
        Assert.Equal("Refund", refund.Method);
        Assert.Equal(4000m, refund.Amount);
    }

    [Fact]
    public async Task Runner_QuittingBeforeStart_ReopensTheErrandWithoutMovingMoney()
    {
        var testDb = new TestDatabase();
        var customerId = Guid.NewGuid();
        var runnerId = Guid.NewGuid();
        var errand = TestData.AcceptedErrand(customerId, runnerId);
        await testDb.SeedAsync(s => s.Errands.Add(errand));

        var escrow = new RecordingEscrowService();
        using var db = testDb.NewContext();
        var handler = new CancelErrandCommandHandler(db, escrow, TestData.Clock, NullLogger<CancelErrandCommandHandler>.Instance);

        await handler.Handle(new CancelErrandCommand { ErrandId = errand.Id, RequestingUserId = runnerId }, CancellationToken.None);

        var saved = await testDb.ReadAsync(r => r.Errands.AsNoTracking().FirstAsync(e => e.Id == errand.Id));
        Assert.Equal(ErrandStatus.Created, saved.Status);
        Assert.Null(saved.RunnerId);
        Assert.Empty(escrow.Calls);
    }

    [Fact]
    public async Task Cancel_WhenTheRefundFails_StillCancelsTheErrand_AndDoesNotThrow()
    {
        // The handler logs a "RECONCILE:" error for a human to follow up on.
        var testDb = new TestDatabase();
        var customerId = Guid.NewGuid();
        var errand = TestData.NewErrand(customerId);
        await testDb.SeedAsync(s => s.Errands.Add(errand));

        var escrow = new RecordingEscrowService { FailRefunds = true };
        using var db = testDb.NewContext();
        var handler = new CancelErrandCommandHandler(db, escrow, TestData.Clock, NullLogger<CancelErrandCommandHandler>.Instance);

        await handler.Handle(new CancelErrandCommand { ErrandId = errand.Id, RequestingUserId = customerId }, CancellationToken.None);

        var saved = await testDb.ReadAsync(r => r.Errands.AsNoTracking().FirstAsync(e => e.Id == errand.Id));
        Assert.Equal(ErrandStatus.Cancelled, saved.Status);
    }

    [Fact]
    public async Task Customer_CancellingTwice_IsRefundedOnlyOnce()
    {
        var testDb = new TestDatabase();
        var customerId = Guid.NewGuid();
        var errand = TestData.NewErrand(customerId);
        await testDb.SeedAsync(s => s.Errands.Add(errand));

        var escrow = new RecordingEscrowService();

        using (var first = testDb.NewContext())
        {
            await new CancelErrandCommandHandler(first, escrow, TestData.Clock, NullLogger<CancelErrandCommandHandler>.Instance)
                .Handle(new CancelErrandCommand { ErrandId = errand.Id, RequestingUserId = customerId }, CancellationToken.None);
        }

        using (var second = testDb.NewContext())
        {
            var handler = new CancelErrandCommandHandler(second, escrow, TestData.Clock, NullLogger<CancelErrandCommandHandler>.Instance);

            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                handler.Handle(new CancelErrandCommand { ErrandId = errand.Id, RequestingUserId = customerId }, CancellationToken.None));
        }

        Assert.Single(escrow.Calls);
    }

    [Fact]
    public async Task Runner_QuittingMidTask_CancelsAndFullyRefundsTheCustomer()
    {
        var testDb = new TestDatabase();
        var customerId = Guid.NewGuid();
        var runnerId = Guid.NewGuid();
        var errand = TestData.InProgressErrand(customerId, runnerId);
        await testDb.SeedAsync(s => s.Errands.Add(errand));

        var escrow = new RecordingEscrowService();
        using var db = testDb.NewContext();
        var handler = new CancelErrandCommandHandler(db, escrow, TestData.Clock, NullLogger<CancelErrandCommandHandler>.Instance);

        await handler.Handle(new CancelErrandCommand { ErrandId = errand.Id, RequestingUserId = runnerId }, CancellationToken.None);

        var saved = await testDb.ReadAsync(r => r.Errands.AsNoTracking().FirstAsync(e => e.Id == errand.Id));
        Assert.Equal(ErrandStatus.Cancelled, saved.Status);

        var refund = Assert.Single(escrow.Calls);
        Assert.Equal(customerId, refund.PartyId);
        Assert.Equal(5000m, refund.Amount);
    }

    [Fact]
    public async Task Cancel_ByAStranger_IsForbidden()
    {
        var testDb = new TestDatabase();
        var errand = TestData.NewErrand();
        await testDb.SeedAsync(s => s.Errands.Add(errand));

        var escrow = new RecordingEscrowService();
        using var db = testDb.NewContext();
        var handler = new CancelErrandCommandHandler(db, escrow, TestData.Clock, NullLogger<CancelErrandCommandHandler>.Instance);

        await Assert.ThrowsAsync<ForbiddenAccessException>(() =>
            handler.Handle(new CancelErrandCommand { ErrandId = errand.Id, RequestingUserId = Guid.NewGuid() }, CancellationToken.None));

        Assert.Empty(escrow.Calls);
    }

    [Fact]
    public async Task Customer_CannotCancelAfterProofIsSubmitted()
    {
        var testDb = new TestDatabase();
        var customerId = Guid.NewGuid();
        var errand = TestData.PendingConfirmationErrand(customerId, Guid.NewGuid());
        await testDb.SeedAsync(s => s.Errands.Add(errand));

        var escrow = new RecordingEscrowService();
        using var db = testDb.NewContext();
        var handler = new CancelErrandCommandHandler(db, escrow, TestData.Clock, NullLogger<CancelErrandCommandHandler>.Instance);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            handler.Handle(new CancelErrandCommand { ErrandId = errand.Id, RequestingUserId = customerId }, CancellationToken.None));

        Assert.Empty(escrow.Calls);
    }
}

public class ExpireUnacceptedErrandsTests
{
    private static ExpireUnacceptedErrandsCommandHandler NewHandler(Errandy.Infrastructure.Persistence.ErrandyDbContext db, RecordingEscrowService escrow) =>
        new(db, escrow, TestData.Clock, NullLogger<ExpireUnacceptedErrandsCommandHandler>.Instance);

    [Fact]
    public async Task Sweep_CancelsAndRefundsOverdueErrands_AndLeavesFreshOnesAlone()
    {
        var testDb = new TestDatabase();
        var overdueCustomer = Guid.NewGuid();

        // Created 3 hours ago, so the 2-hour ASAP deadline passed an hour ago.
        var overdue = TestData.NewErrand(overdueCustomer, cost: 5000m, createdAt: TestData.Now.AddHours(-3));
        var fresh = TestData.NewErrand();
        await testDb.SeedAsync(s => { s.Errands.Add(overdue); s.Errands.Add(fresh); });

        var escrow = new RecordingEscrowService();
        using var db = testDb.NewContext();

        var expired = await NewHandler(db, escrow).Handle(new ExpireUnacceptedErrandsCommand(), CancellationToken.None);

        Assert.Equal(1, expired);
        var savedOverdue = await testDb.ReadAsync(r => r.Errands.AsNoTracking().FirstAsync(e => e.Id == overdue.Id));
        var savedFresh = await testDb.ReadAsync(r => r.Errands.AsNoTracking().FirstAsync(e => e.Id == fresh.Id));
        Assert.Equal(ErrandStatus.Cancelled, savedOverdue.Status);
        Assert.Equal(ErrandStatus.Created, savedFresh.Status);

        var refund = Assert.Single(escrow.Calls);
        Assert.Equal(overdueCustomer, refund.PartyId);
        Assert.Equal(5000m, refund.Amount);
    }

    [Fact]
    public async Task Sweep_IgnoresOverdueErrandsThatAlreadyHaveARunner()
    {
        var testDb = new TestDatabase();
        var errand = TestData.NewErrand(createdAt: TestData.Now.AddHours(-3));
        errand.Accept(Guid.NewGuid(), TestData.Now.AddHours(-2));
        await testDb.SeedAsync(s => s.Errands.Add(errand));

        var escrow = new RecordingEscrowService();
        using var db = testDb.NewContext();

        var expired = await NewHandler(db, escrow).Handle(new ExpireUnacceptedErrandsCommand(), CancellationToken.None);

        Assert.Equal(0, expired);
        Assert.Empty(escrow.Calls);
        var saved = await testDb.ReadAsync(r => r.Errands.AsNoTracking().FirstAsync(e => e.Id == errand.Id));
        Assert.Equal(ErrandStatus.Accepted, saved.Status);
    }

    [Fact]
    public async Task Sweep_WhenTheRefundFails_StillCancelsTheErrandAndDoesNotCrash()
    {
        var testDb = new TestDatabase();
        var errand = TestData.NewErrand(createdAt: TestData.Now.AddHours(-3));
        await testDb.SeedAsync(s => s.Errands.Add(errand));

        var escrow = new RecordingEscrowService { FailRefunds = true };
        using var db = testDb.NewContext();

        var expired = await NewHandler(db, escrow).Handle(new ExpireUnacceptedErrandsCommand(), CancellationToken.None);

        // The handler logs a "RECONCILE:" error for a human to follow up on.
        Assert.Equal(1, expired);
        var saved = await testDb.ReadAsync(r => r.Errands.AsNoTracking().FirstAsync(e => e.Id == errand.Id));
        Assert.Equal(ErrandStatus.Cancelled, saved.Status);
    }
}