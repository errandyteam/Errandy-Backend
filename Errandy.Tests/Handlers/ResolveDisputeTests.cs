using Errandy.Application.Features.Disputes.ResolveDispute;
using Errandy.Domain.Entities;
using Errandy.Domain.Enums;
using Errandy.Infrastructure.Persistence;
using Errandy.Tests.Support;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Errandy.Tests.Handlers;

public class ResolveDisputeTests
{
    private static ResolveDisputeCommandHandler NewHandler(ErrandyDbContext db, RecordingEscrowService escrow) =>
        new(db, escrow, TestData.Clock, NullLogger<ResolveDisputeCommandHandler>.Instance);

    private static async Task<(TestDatabase Db, Errand Errand, Dispute Dispute, Guid CustomerId, Guid RunnerId)> SeedAsync()
    {
        var testDb = new TestDatabase();
        var customerId = Guid.NewGuid();
        var runnerId = Guid.NewGuid();
        var (errand, dispute) = TestData.DisputedErrand(customerId, runnerId, withProof: true);
        await testDb.SeedAsync(s => { s.Errands.Add(errand); s.Disputes.Add(dispute); });
        return (testDb, errand, dispute, customerId, runnerId);
    }

    private static ResolveDisputeCommand Command(Guid disputeId, DisputeResolutionType resolution,
        decimal? refund = null, decimal? release = null) => new()
        {
            DisputeId = disputeId,
            AdminId = Guid.NewGuid(),
            Resolution = resolution,
            RefundToCustomerAmount = refund,
            ReleaseToRunnerAmount = release,
            Notes = "Admin decision."
        };

    [Fact]
    public async Task FullPaymentToRunner_CompletesTheErrand_AndReleasesTheFinalCost()
    {
        var (testDb, errand, dispute, _, runnerId) = await SeedAsync();
        var escrow = new RecordingEscrowService();
        using var db = testDb.NewContext();

        await NewHandler(db, escrow).Handle(Command(dispute.Id, DisputeResolutionType.FullPaymentToRunner), CancellationToken.None);

        var savedErrand = await testDb.ReadAsync(r => r.Errands.AsNoTracking().FirstAsync(e => e.Id == errand.Id));
        var savedDispute = await testDb.ReadAsync(r => r.Disputes.AsNoTracking().FirstAsync(d => d.Id == dispute.Id));
        Assert.Equal(ErrandStatus.Completed, savedErrand.Status);
        Assert.Equal(DisputeStatus.Resolved, savedDispute.Status);

        var call = Assert.Single(escrow.Calls);
        Assert.Equal("Release", call.Method);
        Assert.Equal(runnerId, call.PartyId);
        Assert.Equal(4800m, call.Amount);
    }

    [Fact]
    public async Task FullRefundToCustomer_CancelsTheErrand_AndRefundsTheCustomer()
    {
        var (testDb, errand, dispute, customerId, _) = await SeedAsync();
        var escrow = new RecordingEscrowService();
        using var db = testDb.NewContext();

        await NewHandler(db, escrow).Handle(Command(dispute.Id, DisputeResolutionType.FullRefundToCustomer), CancellationToken.None);

        var savedErrand = await testDb.ReadAsync(r => r.Errands.AsNoTracking().FirstAsync(e => e.Id == errand.Id));
        Assert.Equal(ErrandStatus.Cancelled, savedErrand.Status);

        var call = Assert.Single(escrow.Calls);
        Assert.Equal("Refund", call.Method);
        Assert.Equal(customerId, call.PartyId);
        Assert.Equal(4800m, call.Amount);
    }

    [Fact]
    public async Task PartialRefund_SplitsTheMoney_AndCompletesTheErrand()
    {
        var (testDb, errand, dispute, customerId, _) = await SeedAsync();
        var escrow = new RecordingEscrowService();
        using var db = testDb.NewContext();

        await NewHandler(db, escrow).Handle(
            Command(dispute.Id, DisputeResolutionType.PartialRefundToCustomer, refund: 1000m, release: 3800m),
            CancellationToken.None);

        var savedErrand = await testDb.ReadAsync(r => r.Errands.AsNoTracking().FirstAsync(e => e.Id == errand.Id));
        Assert.Equal(ErrandStatus.Completed, savedErrand.Status);

        var call = Assert.Single(escrow.Calls);
        Assert.Equal("PartialRefund", call.Method);
        Assert.Equal(customerId, call.PartyId);
        Assert.Equal(1000m, call.Amount);
    }

    [Fact]
    public async Task PartialRefund_WithoutBothAmounts_ChangesNothing()
    {
        var (testDb, errand, dispute, _, _) = await SeedAsync();
        var escrow = new RecordingEscrowService();
        using var db = testDb.NewContext();
        var handler = NewHandler(db, escrow);

        await Assert.ThrowsAsync<ArgumentException>(() =>
            handler.Handle(Command(dispute.Id, DisputeResolutionType.PartialRefundToCustomer, refund: 1000m), CancellationToken.None));

        var savedErrand = await testDb.ReadAsync(r => r.Errands.AsNoTracking().FirstAsync(e => e.Id == errand.Id));
        var savedDispute = await testDb.ReadAsync(r => r.Disputes.AsNoTracking().FirstAsync(d => d.Id == dispute.Id));
        Assert.Equal(ErrandStatus.Disputed, savedErrand.Status);
        Assert.Equal(DisputeStatus.Open, savedDispute.Status);
        Assert.Empty(escrow.Calls);
    }

    [Fact]
    public async Task WhenThePayoutFails_TheDecisionIsStillSaved_AndNothingCrashes()
    {
        // The handler logs a "RECONCILE:" error for a human to follow up on.
        var (testDb, errand, dispute, _, _) = await SeedAsync();
        var escrow = new RecordingEscrowService { FailRefunds = true };
        using var db = testDb.NewContext();

        await NewHandler(db, escrow).Handle(Command(dispute.Id, DisputeResolutionType.FullRefundToCustomer), CancellationToken.None);

        var savedErrand = await testDb.ReadAsync(r => r.Errands.AsNoTracking().FirstAsync(e => e.Id == errand.Id));
        var savedDispute = await testDb.ReadAsync(r => r.Disputes.AsNoTracking().FirstAsync(d => d.Id == dispute.Id));
        Assert.Equal(ErrandStatus.Cancelled, savedErrand.Status);
        Assert.Equal(DisputeStatus.Resolved, savedDispute.Status);
    }

    [Fact]
    public async Task ResolvingTheSameDisputeTwice_MovesMoneyOnlyOnce()
    {
        var (testDb, _, dispute, _, _) = await SeedAsync();
        var escrow = new RecordingEscrowService();

        using (var first = testDb.NewContext())
        {
            await NewHandler(first, escrow).Handle(Command(dispute.Id, DisputeResolutionType.FullPaymentToRunner), CancellationToken.None);
        }

        using (var second = testDb.NewContext())
        {
            var handler = NewHandler(second, escrow);

            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                handler.Handle(Command(dispute.Id, DisputeResolutionType.FullPaymentToRunner), CancellationToken.None));
        }

        Assert.Single(escrow.Calls);
    }
}