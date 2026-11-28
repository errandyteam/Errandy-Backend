using Errandy.Application.Common.Exceptions;
using Errandy.Application.Features.Errands.AcceptErrand;
using Errandy.Application.Features.Errands.StartErrand;
using Errandy.Domain.Enums;
using Errandy.Tests.Support;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Errandy.Tests.Handlers;

public class AcceptAndStartTests
{
    [Fact]
    public async Task Accept_OwnErrand_IsForbidden_EvenForAnApprovedRunner()
    {
        var testDb = new TestDatabase();
        var userId = Guid.NewGuid();
        var errand = TestData.NewErrand(customerId: userId);
        await testDb.SeedAsync(s =>
        {
            s.Errands.Add(errand);
            s.RunnerProfiles.Add(TestData.Runner(userId, KycStatus.Approved));
        });

        using var db = testDb.NewContext();
        var handler = new AcceptErrandCommandHandler(db, TestData.Clock);

        await Assert.ThrowsAsync<ForbiddenAccessException>(() =>
            handler.Handle(new AcceptErrandCommand { ErrandId = errand.Id, RunnerId = userId }, CancellationToken.None));

   
        var saved = await testDb.ReadAsync(r => r.Errands.AsNoTracking().FirstAsync(e => e.Id == errand.Id));
        Assert.Equal(ErrandStatus.Created, saved.Status);
    }

    [Fact]
    public async Task Accept_ByUserWithNoRunnerProfile_IsForbidden()
    {
        var testDb = new TestDatabase();
        var errand = TestData.NewErrand();
        await testDb.SeedAsync(s => s.Errands.Add(errand));

        using var db = testDb.NewContext();
        var handler = new AcceptErrandCommandHandler(db, TestData.Clock);

        await Assert.ThrowsAsync<ForbiddenAccessException>(() =>
            handler.Handle(new AcceptErrandCommand { ErrandId = errand.Id, RunnerId = Guid.NewGuid() }, CancellationToken.None));
    }

    [Theory]
    [InlineData(KycStatus.Pending)]
    [InlineData(KycStatus.Rejected)]
    public async Task Accept_ByRunnerWhoIsNotApproved_IsForbidden(KycStatus status)
    {
        var testDb = new TestDatabase();
        var runnerId = Guid.NewGuid();
        var errand = TestData.NewErrand();
        await testDb.SeedAsync(s =>
        {
            s.Errands.Add(errand);
            s.RunnerProfiles.Add(TestData.Runner(runnerId, status));
        });

        using var db = testDb.NewContext();
        var handler = new AcceptErrandCommandHandler(db, TestData.Clock);

        await Assert.ThrowsAsync<ForbiddenAccessException>(() =>
    handler.Handle(new AcceptErrandCommand { ErrandId = errand.Id, RunnerId = runnerId }, CancellationToken.None));

        var saved = await testDb.ReadAsync(r => r.Errands.AsNoTracking().FirstAsync(e => e.Id == errand.Id));
        Assert.Equal(ErrandStatus.Created, saved.Status);
        Assert.Null(saved.RunnerId);
    }

    [Fact]
    public async Task Accept_ByApprovedRunner_Succeeds()
    {
        var testDb = new TestDatabase();
        var runnerId = Guid.NewGuid();
        var errand = TestData.NewErrand();
        await testDb.SeedAsync(s =>
        {
            s.Errands.Add(errand);
            s.RunnerProfiles.Add(TestData.Runner(runnerId, KycStatus.Approved));
        });

        using var db = testDb.NewContext();
        var handler = new AcceptErrandCommandHandler(db, TestData.Clock);

        await handler.Handle(new AcceptErrandCommand { ErrandId = errand.Id, RunnerId = runnerId }, CancellationToken.None);

        var saved = await testDb.ReadAsync(r => r.Errands.AsNoTracking().FirstAsync(e => e.Id == errand.Id));
        Assert.Equal(ErrandStatus.Accepted, saved.Status);
        Assert.Equal(runnerId, saved.RunnerId);
    }

    [Fact]
    public async Task Start_ByRunnerWhoLostApprovalAfterAccepting_IsForbidden()
    {
        var testDb = new TestDatabase();
        var customerId = Guid.NewGuid();
        var runnerId = Guid.NewGuid();
        var errand = TestData.AcceptedErrand(customerId, runnerId);
        await testDb.SeedAsync(s =>
        {
            s.Errands.Add(errand);
            s.RunnerProfiles.Add(TestData.Runner(runnerId, KycStatus.Rejected));
        });

        using var db = testDb.NewContext();
        var handler = new StartErrandCommandHandler(db, TestData.Clock);

        await Assert.ThrowsAsync<ForbiddenAccessException>(() =>
            handler.Handle(new StartErrandCommand { ErrandId = errand.Id, RunnerId = runnerId }, CancellationToken.None));

        var saved = await testDb.ReadAsync(r => r.Errands.AsNoTracking().FirstAsync(e => e.Id == errand.Id));
        Assert.Equal(ErrandStatus.Accepted, saved.Status);
    }

    [Fact]
    public async Task Start_ByApprovedRunner_MovesErrandToInProgress()
    {
        var testDb = new TestDatabase();
        var customerId = Guid.NewGuid();
        var runnerId = Guid.NewGuid();
        var errand = TestData.AcceptedErrand(customerId, runnerId);
        await testDb.SeedAsync(s =>
        {
            s.Errands.Add(errand);
            s.RunnerProfiles.Add(TestData.Runner(runnerId, KycStatus.Approved));
        });

        using var db = testDb.NewContext();
        var handler = new StartErrandCommandHandler(db, TestData.Clock);

        await handler.Handle(new StartErrandCommand { ErrandId = errand.Id, RunnerId = runnerId }, CancellationToken.None);

        var saved = await testDb.ReadAsync(r => r.Errands.AsNoTracking().FirstAsync(e => e.Id == errand.Id));
        Assert.Equal(ErrandStatus.InProgress, saved.Status);
    }
}