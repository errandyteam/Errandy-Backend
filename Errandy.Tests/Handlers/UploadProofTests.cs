using Errandy.Application.Common.Exceptions;
using Errandy.Application.Features.Errands.GetProofFile;
using Errandy.Application.Features.Errands.UploadProof;
using Errandy.Domain.Entities;
using Errandy.Domain.Enums;
using Errandy.Domain.Exceptions;
using Errandy.Infrastructure.Persistence;
using Errandy.Tests.Support;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Errandy.Tests.Handlers;

public class UploadProofTests
{
    private static UploadProofCommandHandler NewHandler(ErrandyDbContext db, InMemoryFileStorage storage) =>
    new(db, TestData.Clock, storage, NullLogger<UploadProofCommandHandler>.Instance);

    [Fact]
    public async Task Upload_StoresBothFiles_RecordsTheirKeys_AndSubmitsForConfirmation()
    {
        var testDb = new TestDatabase();
        var runnerId = Guid.NewGuid();
        var errand = TestData.InProgressErrand(Guid.NewGuid(), runnerId);
        await testDb.SeedAsync(s => s.Errands.Add(errand));

        var storage = new InMemoryFileStorage();
        using var db = testDb.NewContext();

        await NewHandler(db, storage).Handle(new UploadProofCommand
        {
            ErrandId = errand.Id,
            RunnerId = runnerId,
            ImageContent = TestFiles.Jpeg(),
            ReceiptContent = TestFiles.Png(),
            FinalCost = 4800m
        }, CancellationToken.None);

        var proof = await testDb.ReadAsync(r => r.Proofs.AsNoTracking().SingleAsync(p => p.ErrandId == errand.Id));
        var saved = await testDb.ReadAsync(r => r.Errands.AsNoTracking().FirstAsync(e => e.Id == errand.Id));

        Assert.Equal(2, storage.Keys.Count);
        Assert.StartsWith($"proofs/{errand.Id}/", proof.ImageUrl);
        Assert.EndsWith(".jpg", proof.ImageUrl);
        Assert.EndsWith(".png", proof.ReceiptUrl);
        Assert.Contains(proof.ImageUrl, storage.Keys);
        Assert.Equal(ErrandStatus.PendingConfirmation, saved.Status);
        Assert.Equal(4800m, saved.FinalCost);
    }

    [Fact]
    public async Task Upload_WithoutAReceipt_StoresOnlyThePhoto()
    {
        var testDb = new TestDatabase();
        var runnerId = Guid.NewGuid();
        var errand = TestData.InProgressErrand(Guid.NewGuid(), runnerId);
        await testDb.SeedAsync(s => s.Errands.Add(errand));

        var storage = new InMemoryFileStorage();
        using var db = testDb.NewContext();

        await NewHandler(db, storage).Handle(new UploadProofCommand
        {
            ErrandId = errand.Id,
            RunnerId = runnerId,
            ImageContent = TestFiles.WebP(),
            FinalCost = 4800m
        }, CancellationToken.None);

        var proof = await testDb.ReadAsync(r => r.Proofs.AsNoTracking().SingleAsync(p => p.ErrandId == errand.Id));
        Assert.Single(storage.Keys);
        Assert.Null(proof.ReceiptUrl);
    }

    [Fact]
    public async Task Upload_ByTheWrongRunner_IsForbidden_AndWritesNothing()
    {
        var testDb = new TestDatabase();
        var errand = TestData.InProgressErrand(Guid.NewGuid(), Guid.NewGuid());
        await testDb.SeedAsync(s => s.Errands.Add(errand));

        var storage = new InMemoryFileStorage();
        using var db = testDb.NewContext();

        await Assert.ThrowsAsync<ForbiddenAccessException>(() =>
            NewHandler(db, storage).Handle(new UploadProofCommand
            {
                ErrandId = errand.Id,
                RunnerId = Guid.NewGuid(),
                ImageContent = TestFiles.Jpeg(),
                FinalCost = 4800m
            }, CancellationToken.None));

        Assert.Empty(storage.Keys);
    }

    [Fact]
    public async Task Upload_BeforeTheErrandIsStarted_IsRefused_AndWritesNothing()
    {
        var testDb = new TestDatabase();
        var runnerId = Guid.NewGuid();
        var errand = TestData.AcceptedErrand(Guid.NewGuid(), runnerId);
        await testDb.SeedAsync(s => s.Errands.Add(errand));

        var storage = new InMemoryFileStorage();
        using var db = testDb.NewContext();

        await Assert.ThrowsAsync<InvalidErrandStateException>(() =>
            NewHandler(db, storage).Handle(new UploadProofCommand
            {
                ErrandId = errand.Id,
                RunnerId = runnerId,
                ImageContent = TestFiles.Jpeg(),
                FinalCost = 4800m
            }, CancellationToken.None));

        Assert.Empty(storage.Keys);
    }

    [Fact]
    public async Task Upload_WhenTheSecondFileFailsToSave_RemovesTheFirstAndLeavesTheErrandUnchanged()
    {
        var testDb = new TestDatabase();
        var runnerId = Guid.NewGuid();
        var errand = TestData.InProgressErrand(Guid.NewGuid(), runnerId);
        await testDb.SeedAsync(s => s.Errands.Add(errand));

        var storage = new InMemoryFileStorage { FailOnSaveNumber = 2 };
        using var db = testDb.NewContext();

        await Assert.ThrowsAsync<IOException>(() =>
            NewHandler(db, storage).Handle(new UploadProofCommand
            {
                ErrandId = errand.Id,
                RunnerId = runnerId,
                ImageContent = TestFiles.Jpeg(),
                ReceiptContent = TestFiles.Png(),
                FinalCost = 4800m
            }, CancellationToken.None));

        Assert.Empty(storage.Keys);
        var saved = await testDb.ReadAsync(r => r.Errands.AsNoTracking().FirstAsync(e => e.Id == errand.Id));
        Assert.Equal(ErrandStatus.InProgress, saved.Status);
    }
}

public class GetProofFileTests
{
    private static async Task<(TestDatabase Db, Errand Errand, Guid CustomerId, Guid RunnerId, InMemoryFileStorage Storage)> SeedErrandWithStoredProofAsync()
    {
        var testDb = new TestDatabase();
        var customerId = Guid.NewGuid();
        var runnerId = Guid.NewGuid();
        var storage = new InMemoryFileStorage();

        var errand = TestData.InProgressErrand(customerId, runnerId);
        var key = await storage.SaveAsync($"proofs/{errand.Id}", "jpg", TestFiles.Jpeg());
        var proof = Proof.Create(errand.Id, key, null, TestData.Now);
        errand.AttachProofAndSubmitForConfirmation(runnerId, proof, 4800m, TestData.Now);

        await testDb.SeedAsync(s => s.Errands.Add(errand));
        return (testDb, errand, customerId, runnerId, storage);
    }

    [Fact]
    public async Task TheCustomerCanViewTheProofPhoto()
    {
        var (testDb, errand, customerId, _, storage) = await SeedErrandWithStoredProofAsync();
        using var db = testDb.NewContext();

        var result = await new GetProofFileQueryHandler(db, storage).Handle(
            new GetProofFileQuery { ErrandId = errand.Id, RequestingUserId = customerId, Kind = ProofFileKind.Image },
            CancellationToken.None);

        using var content = result.Content;
        Assert.Equal("image/jpeg", result.ContentType);
        Assert.Equal(2048, content.Length);
    }

    [Fact]
    public async Task TheRunnerCanViewTheirOwnProofPhoto()
    {
        var (testDb, errand, _, runnerId, storage) = await SeedErrandWithStoredProofAsync();
        using var db = testDb.NewContext();

        var result = await new GetProofFileQueryHandler(db, storage).Handle(
            new GetProofFileQuery { ErrandId = errand.Id, RequestingUserId = runnerId, Kind = ProofFileKind.Image },
            CancellationToken.None);

        using var content = result.Content;
        Assert.Equal("image/jpeg", result.ContentType);
    }

    [Fact]
    public async Task AnAdminCanViewAnyProofPhoto()
    {
        var (testDb, errand, _, _, storage) = await SeedErrandWithStoredProofAsync();
        using var db = testDb.NewContext();

        var result = await new GetProofFileQueryHandler(db, storage).Handle(
            new GetProofFileQuery { ErrandId = errand.Id, RequestingUserId = Guid.NewGuid(), IsAdmin = true, Kind = ProofFileKind.Image },
            CancellationToken.None);

        using var content = result.Content;
        Assert.Equal("image/jpeg", result.ContentType);
    }

    [Fact]
    public async Task AStrangerCannotViewTheProofPhoto()
    {
        var (testDb, errand, _, _, storage) = await SeedErrandWithStoredProofAsync();
        using var db = testDb.NewContext();

        await Assert.ThrowsAsync<ForbiddenAccessException>(() =>
            new GetProofFileQueryHandler(db, storage).Handle(
                new GetProofFileQuery { ErrandId = errand.Id, RequestingUserId = Guid.NewGuid(), Kind = ProofFileKind.Image },
                CancellationToken.None));
    }

    [Fact]
    public async Task AskingForAReceiptThatWasNeverUploaded_IsNotFound()
    {
        var (testDb, errand, customerId, _, storage) = await SeedErrandWithStoredProofAsync();
        using var db = testDb.NewContext();

        await Assert.ThrowsAsync<NotFoundException>(() =>
            new GetProofFileQueryHandler(db, storage).Handle(
                new GetProofFileQuery { ErrandId = errand.Id, RequestingUserId = customerId, Kind = ProofFileKind.Receipt },
                CancellationToken.None));
    }

    [Fact]
    public async Task AnOldProofThatHoldsAWebLink_IsNotFound()
    {
        var testDb = new TestDatabase();
        var customerId = Guid.NewGuid();
        var errand = TestData.PendingConfirmationErrand(customerId, Guid.NewGuid()); // proof holds an https:// link
        await testDb.SeedAsync(s => s.Errands.Add(errand));
        using var db = testDb.NewContext();

        await Assert.ThrowsAsync<NotFoundException>(() =>
            new GetProofFileQueryHandler(db, new InMemoryFileStorage()).Handle(
                new GetProofFileQuery { ErrandId = errand.Id, RequestingUserId = customerId, Kind = ProofFileKind.Image },
                CancellationToken.None));
    }
}