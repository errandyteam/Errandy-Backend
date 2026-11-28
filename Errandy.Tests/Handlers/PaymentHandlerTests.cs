using Errandy.Application.Common.Exceptions;
using Errandy.Application.Features.Errands.AdminForceComplete;
using Errandy.Application.Features.Errands.ConfirmCompletion;
using Errandy.Application.Features.Errands.RespondToPriceAdjustment;
using Errandy.Domain.Entities;
using Errandy.Domain.Enums;
using Errandy.Domain.Exceptions;
using Errandy.Infrastructure.Persistence;
using Errandy.Tests.Support;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Errandy.Tests.Handlers;

public class ConfirmCompletionTests
{
    private static ConfirmCompletionCommandHandler NewHandler(ErrandyDbContext db, RecordingEscrowService escrow) =>
        new(db, escrow, TestData.Clock, NullLogger<ConfirmCompletionCommandHandler>.Instance);

    [Fact]
    public async Task TheCustomerConfirming_CompletesTheErrand_AndPaysTheRunnerTheFinalCost()
    {
        var testDb = new TestDatabase();
        var customerId = Guid.NewGuid();
        var runnerId = Guid.NewGuid();
        var errand = TestData.PendingConfirmationErrand(customerId, runnerId);
        await testDb.SeedAsync(s => s.Errands.Add(errand));

        var escrow = new RecordingEscrowService();
        using var db = testDb.NewContext();

        await NewHandler(db, escrow).Handle(
            new ConfirmCompletionCommand { ErrandId = errand.Id, CustomerId = customerId }, CancellationToken.None);

        var saved = await testDb.ReadAsync(r => r.Errands.AsNoTracking().FirstAsync(e => e.Id == errand.Id));
        Assert.Equal(ErrandStatus.Completed, saved.Status);

        var call = Assert.Single(escrow.Calls);
        Assert.Equal("Release", call.Method);
        Assert.Equal(runnerId, call.PartyId);
        Assert.Equal(4800m, call.Amount);
    }

    [Fact]
    public async Task SomeoneElseConfirming_IsForbidden_AndNothingIsPaid()
    {
        var testDb = new TestDatabase();
        var errand = TestData.PendingConfirmationErrand(Guid.NewGuid(), Guid.NewGuid());
        await testDb.SeedAsync(s => s.Errands.Add(errand));

        var escrow = new RecordingEscrowService();
        using var db = testDb.NewContext();
        var handler = NewHandler(db, escrow);

        await Assert.ThrowsAsync<ForbiddenAccessException>(() =>
            handler.Handle(new ConfirmCompletionCommand { ErrandId = errand.Id, CustomerId = Guid.NewGuid() }, CancellationToken.None));

        Assert.Empty(escrow.Calls);
    }

    [Fact]
    public async Task Confirming_BeforeProofIsSubmitted_IsRefused()
    {
        var testDb = new TestDatabase();
        var customerId = Guid.NewGuid();
        var errand = TestData.InProgressErrand(customerId, Guid.NewGuid());
        await testDb.SeedAsync(s => s.Errands.Add(errand));

        var escrow = new RecordingEscrowService();
        using var db = testDb.NewContext();
        var handler = NewHandler(db, escrow);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            handler.Handle(new ConfirmCompletionCommand { ErrandId = errand.Id, CustomerId = customerId }, CancellationToken.None));

        Assert.Empty(escrow.Calls);
    }

    [Fact]
    public async Task Confirming_Twice_PaysOnlyOnce()
    {
        var testDb = new TestDatabase();
        var customerId = Guid.NewGuid();
        var errand = TestData.PendingConfirmationErrand(customerId, Guid.NewGuid());
        await testDb.SeedAsync(s => s.Errands.Add(errand));

        var escrow = new RecordingEscrowService();

        using (var first = testDb.NewContext())
        {
            await NewHandler(first, escrow).Handle(
                new ConfirmCompletionCommand { ErrandId = errand.Id, CustomerId = customerId }, CancellationToken.None);
        }

        using (var second = testDb.NewContext())
        {
            var handler = NewHandler(second, escrow);

            await Assert.ThrowsAsync<InvalidErrandStateException>(() =>
                handler.Handle(new ConfirmCompletionCommand { ErrandId = errand.Id, CustomerId = customerId }, CancellationToken.None));
        }

        Assert.Single(escrow.Calls);
    }

    [Fact]
    public async Task WhenThePayoutFails_TheConfirmationStillStands_AndNothingCrashes()
    {
        // The handler logs a "RECONCILE:" error for a human to follow up on.
        var testDb = new TestDatabase();
        var customerId = Guid.NewGuid();
        var errand = TestData.PendingConfirmationErrand(customerId, Guid.NewGuid());
        await testDb.SeedAsync(s => s.Errands.Add(errand));

        var escrow = new RecordingEscrowService { FailReleases = true };
        using var db = testDb.NewContext();

        await NewHandler(db, escrow).Handle(
            new ConfirmCompletionCommand { ErrandId = errand.Id, CustomerId = customerId }, CancellationToken.None);

        var saved = await testDb.ReadAsync(r => r.Errands.AsNoTracking().FirstAsync(e => e.Id == errand.Id));
        Assert.Equal(ErrandStatus.Completed, saved.Status);
    }
}

public class AdminForceCompleteTests
{
    private static AdminForceCompleteErrandCommandHandler NewHandler(ErrandyDbContext db, RecordingEscrowService escrow) =>
        new(db, escrow, TestData.Clock, NullLogger<AdminForceCompleteErrandCommandHandler>.Instance);

    private static AdminForceCompleteErrandCommand Command(Guid errandId) =>
        new() { ErrandId = errandId, AdminId = Guid.NewGuid(), Reason = "Customer unreachable." };

    [Fact]
    public async Task ForceCompleting_AnErrandWithNoProof_PaysTheEstimatedCost()
    {
        var testDb = new TestDatabase();
        var runnerId = Guid.NewGuid();
        var errand = TestData.InProgressErrand(Guid.NewGuid(), runnerId);
        await testDb.SeedAsync(s => s.Errands.Add(errand));

        var escrow = new RecordingEscrowService();
        using var db = testDb.NewContext();

        await NewHandler(db, escrow).Handle(Command(errand.Id), CancellationToken.None);

        var saved = await testDb.ReadAsync(r => r.Errands.AsNoTracking().FirstAsync(e => e.Id == errand.Id));
        Assert.Equal(ErrandStatus.Completed, saved.Status);

        var call = Assert.Single(escrow.Calls);
        Assert.Equal("Release", call.Method);
        Assert.Equal(runnerId, call.PartyId);
        Assert.Equal(5000m, call.Amount);
    }

    [Fact]
    public async Task ForceCompleting_AnErrandWithNoRunner_IsRefused()
    {
        var testDb = new TestDatabase();
        var errand = TestData.NewErrand();
        await testDb.SeedAsync(s => s.Errands.Add(errand));

        var escrow = new RecordingEscrowService();
        using var db = testDb.NewContext();
        var handler = NewHandler(db, escrow);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            handler.Handle(Command(errand.Id), CancellationToken.None));

        Assert.Empty(escrow.Calls);
    }

    [Fact]
    public async Task WhenThePayoutFails_TheErrandIsStillCompleted_AndNothingCrashes()
    {
        var testDb = new TestDatabase();
        var errand = TestData.InProgressErrand(Guid.NewGuid(), Guid.NewGuid());
        await testDb.SeedAsync(s => s.Errands.Add(errand));

        var escrow = new RecordingEscrowService { FailReleases = true };
        using var db = testDb.NewContext();

        await NewHandler(db, escrow).Handle(Command(errand.Id), CancellationToken.None);

        var saved = await testDb.ReadAsync(r => r.Errands.AsNoTracking().FirstAsync(e => e.Id == errand.Id));
        Assert.Equal(ErrandStatus.Completed, saved.Status);
    }
}

public class PriceAdjustmentResponseTests
{
    private static RespondToPriceAdjustmentCommandHandler NewHandler(ErrandyDbContext db, RecordingEscrowService escrow) =>
        new(db, escrow, TestData.Clock, NullLogger<RespondToPriceAdjustmentCommandHandler>.Instance);

    /// <summary>An in-progress errand costing 5000, where the runner has asked for 6000.</summary>
    private static async Task<(TestDatabase Db, Errand Errand, Guid CustomerId)> SeedWithProposalAsync()
    {
        var testDb = new TestDatabase();
        var customerId = Guid.NewGuid();
        var runnerId = Guid.NewGuid();
        var errand = TestData.InProgressErrand(customerId, runnerId);
        errand.ProposePriceAdjustment(runnerId, 6000m, "Traffic was worse than expected", TestData.Now);
        await testDb.SeedAsync(s => s.Errands.Add(errand));
        return (testDb, errand, customerId);
    }

    [Fact]
    public async Task Approving_UpdatesTheCost_ClearsTheProposal_AndAdjustsTheLockedMoney()
    {
        var (testDb, errand, customerId) = await SeedWithProposalAsync();
        var escrow = new RecordingEscrowService();
        using var db = testDb.NewContext();

        await NewHandler(db, escrow).Handle(
            new RespondToPriceAdjustmentCommand { ErrandId = errand.Id, CustomerId = customerId, Approve = true },
            CancellationToken.None);

        var saved = await testDb.ReadAsync(r => r.Errands.AsNoTracking().FirstAsync(e => e.Id == errand.Id));
        Assert.Equal(6000m, saved.EstimatedCost);
        Assert.Null(saved.ProposedCost);

        var call = Assert.Single(escrow.Calls);
        Assert.Equal("Adjust", call.Method);
        Assert.Equal(6000m, call.Amount);
    }

    [Fact]
    public async Task Approving_WhenTheMoneyAdjustmentIsRefused_ChangesNothing_AndTheProposalStaysPending()
    {
        // For example: the customer's balance can't cover the higher price.
        var (testDb, errand, customerId) = await SeedWithProposalAsync();
        var escrow = new RecordingEscrowService { FailAdjustments = true };
        using var db = testDb.NewContext();
        var handler = NewHandler(db, escrow);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            handler.Handle(
                new RespondToPriceAdjustmentCommand { ErrandId = errand.Id, CustomerId = customerId, Approve = true },
                CancellationToken.None));

        var saved = await testDb.ReadAsync(r => r.Errands.AsNoTracking().FirstAsync(e => e.Id == errand.Id));
        Assert.Equal(5000m, saved.EstimatedCost);
        Assert.Equal(6000m, saved.ProposedCost);
        Assert.Empty(escrow.Calls);
    }

    [Fact]
    public async Task Rejecting_KeepsTheOriginalCost_AndMovesNoMoney()
    {
        var (testDb, errand, customerId) = await SeedWithProposalAsync();
        var escrow = new RecordingEscrowService();
        using var db = testDb.NewContext();

        await NewHandler(db, escrow).Handle(
            new RespondToPriceAdjustmentCommand { ErrandId = errand.Id, CustomerId = customerId, Approve = false },
            CancellationToken.None);

        var saved = await testDb.ReadAsync(r => r.Errands.AsNoTracking().FirstAsync(e => e.Id == errand.Id));
        Assert.Equal(5000m, saved.EstimatedCost);
        Assert.Null(saved.ProposedCost);
        Assert.Empty(escrow.Calls);
    }

    [Fact]
    public async Task SomeoneElseResponding_IsForbidden()
    {
        var (testDb, errand, _) = await SeedWithProposalAsync();
        var escrow = new RecordingEscrowService();
        using var db = testDb.NewContext();
        var handler = NewHandler(db, escrow);

        await Assert.ThrowsAsync<ForbiddenAccessException>(() =>
            handler.Handle(
                new RespondToPriceAdjustmentCommand { ErrandId = errand.Id, CustomerId = Guid.NewGuid(), Approve = true },
                CancellationToken.None));

        Assert.Empty(escrow.Calls);
    }

    [Fact]
    public async Task Approving_WhenThereIsNoProposal_IsRefused_AndMovesNoMoney()
    {
        var testDb = new TestDatabase();
        var customerId = Guid.NewGuid();
        var errand = TestData.InProgressErrand(customerId, Guid.NewGuid());
        await testDb.SeedAsync(s => s.Errands.Add(errand));

        var escrow = new RecordingEscrowService();
        using var db = testDb.NewContext();
        var handler = NewHandler(db, escrow);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            handler.Handle(
                new RespondToPriceAdjustmentCommand { ErrandId = errand.Id, CustomerId = customerId, Approve = true },
                CancellationToken.None));

        Assert.Empty(escrow.Calls);
    }
}