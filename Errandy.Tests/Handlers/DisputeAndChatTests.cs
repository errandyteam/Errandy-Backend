using Errandy.Application.Common.Exceptions;
using Errandy.Application.Features.Chat.SendMessage;
using Errandy.Application.Features.Disputes.AddDisputeEvidence;
using Errandy.Application.Features.Disputes.RejectDispute;
using Errandy.Domain.Enums;
using Errandy.Tests.Support;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Errandy.Tests.Handlers;

public class RejectDisputeTests
{
    [Fact]
    public async Task Reject_WhenProofWasSubmitted_ReturnsErrandToPendingConfirmation_WithAFreshWindow()
    {
        var testDb = new TestDatabase();
        var (errand, dispute) = TestData.DisputedErrand(Guid.NewGuid(), Guid.NewGuid(), withProof: true);
        await testDb.SeedAsync(s => { s.Errands.Add(errand); s.Disputes.Add(dispute); });

        // The admin rejects the dispute 30 hours after the proof was submitted.
        var rejectedAt = TestData.Now.AddHours(30);
        using var db = testDb.NewContext();
        var handler = new RejectDisputeCommandHandler(db, new FixedDateTimeProvider(rejectedAt));

        await handler.Handle(
            new RejectDisputeCommand { DisputeId = dispute.Id, AdminId = Guid.NewGuid(), Notes = "No evidence of poor service." },
            CancellationToken.None);

        var savedDispute = await testDb.ReadAsync(r => r.Disputes.AsNoTracking().FirstAsync(d => d.Id == dispute.Id));
        var savedErrand = await testDb.ReadAsync(r => r.Errands.AsNoTracking().FirstAsync(e => e.Id == errand.Id));
        Assert.Equal(DisputeStatus.Rejected, savedDispute.Status);
        Assert.Equal(ErrandStatus.PendingConfirmation, savedErrand.Status);

        // The customer gets the full response time again, counted from the rejection.
        Assert.Equal(rejectedAt, savedErrand.PendingConfirmationAt);
    }

    [Fact]
    public async Task Reject_WhenNoProofYet_ReturnsErrandToInProgress()
    {
        var testDb = new TestDatabase();
        var (errand, dispute) = TestData.DisputedErrand(Guid.NewGuid(), Guid.NewGuid(), withProof: false);
        await testDb.SeedAsync(s => { s.Errands.Add(errand); s.Disputes.Add(dispute); });

        using var db = testDb.NewContext();
        var handler = new RejectDisputeCommandHandler(db, TestData.Clock);

        await handler.Handle(
            new RejectDisputeCommand { DisputeId = dispute.Id, AdminId = Guid.NewGuid(), Notes = "Runner is still on the way." },
            CancellationToken.None);

        var savedErrand = await testDb.ReadAsync(r => r.Errands.AsNoTracking().FirstAsync(e => e.Id == errand.Id));
        Assert.Equal(ErrandStatus.InProgress, savedErrand.Status);
        Assert.Null(savedErrand.PendingConfirmationAt);
    }

    [Fact]
    public async Task Reject_UnknownDispute_IsNotFound()
    {
        var testDb = new TestDatabase();
        using var db = testDb.NewContext();
        var handler = new RejectDisputeCommandHandler(db, TestData.Clock);

        await Assert.ThrowsAsync<NotFoundException>(() =>
            handler.Handle(
                new RejectDisputeCommand { DisputeId = Guid.NewGuid(), AdminId = Guid.NewGuid(), Notes = "x" },
                CancellationToken.None));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void RejectValidator_RequiresAReason(string notes)
    {
        var result = new RejectDisputeCommandValidator().Validate(
            new RejectDisputeCommand { DisputeId = Guid.NewGuid(), AdminId = Guid.NewGuid(), Notes = notes });

        Assert.False(result.IsValid);
    }
}

public class DisputeEvidenceTests
{
    [Fact]
    public async Task AddEvidence_ByTheRunner_IsSavedAlongsideTheExistingEvidence()
    {
        var testDb = new TestDatabase();
        var runnerId = Guid.NewGuid();
        var (errand, dispute) = TestData.DisputedErrand(Guid.NewGuid(), runnerId, withProof: false);
        await testDb.SeedAsync(s => { s.Errands.Add(errand); s.Disputes.Add(dispute); });

        using var db = testDb.NewContext();
        var handler = new AddDisputeEvidenceCommandHandler(db);

        await handler.Handle(new AddDisputeEvidenceCommand
        {
            DisputeId = dispute.Id,
            UserId = runnerId,
            EvidenceUrls = new List<string> { "https://example.com/evidence2.jpg" }
        }, CancellationToken.None);

        var saved = await testDb.ReadAsync(r => r.Disputes.AsNoTracking().FirstAsync(d => d.Id == dispute.Id));
        Assert.Equal(2, saved.EvidenceUrls.Count);
        Assert.Contains("https://example.com/evidence2.jpg", saved.EvidenceUrls);
    }

    [Fact]
    public async Task AddEvidence_ByAStranger_IsForbidden()
    {
        var testDb = new TestDatabase();
        var (errand, dispute) = TestData.DisputedErrand(Guid.NewGuid(), Guid.NewGuid(), withProof: false);
        await testDb.SeedAsync(s => { s.Errands.Add(errand); s.Disputes.Add(dispute); });

        using var db = testDb.NewContext();
        var handler = new AddDisputeEvidenceCommandHandler(db);

        await Assert.ThrowsAsync<ForbiddenAccessException>(() =>
            handler.Handle(new AddDisputeEvidenceCommand
            {
                DisputeId = dispute.Id,
                UserId = Guid.NewGuid(),
                EvidenceUrls = new List<string> { "https://example.com/x.jpg" }
            }, CancellationToken.None));
    }

    [Fact]
    public async Task AddEvidence_ToAClosedDispute_IsRefused()
    {
        var testDb = new TestDatabase();
        var customerId = Guid.NewGuid();
        var (errand, dispute) = TestData.DisputedErrand(customerId, Guid.NewGuid(), withProof: false);
        dispute.Reject(Guid.NewGuid(), "No merit.", TestData.Now);
        errand.ReopenAfterDisputeRejected();
        await testDb.SeedAsync(s => { s.Errands.Add(errand); s.Disputes.Add(dispute); });

        using var db = testDb.NewContext();
        var handler = new AddDisputeEvidenceCommandHandler(db);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            handler.Handle(new AddDisputeEvidenceCommand
            {
                DisputeId = dispute.Id,
                UserId = customerId,
                EvidenceUrls = new List<string> { "https://example.com/late.jpg" }
            }, CancellationToken.None));
    }
}

public class ChatMaskingTests
{
    [Fact]
    public async Task SendMessage_HidesPhoneNumbersBeforeSaving()
    {
        var testDb = new TestDatabase();
        var runnerId = Guid.NewGuid();
        var errand = TestData.AcceptedErrand(Guid.NewGuid(), runnerId);
        await testDb.SeedAsync(s => s.Errands.Add(errand));

        using var db = testDb.NewContext();
        var handler = new SendMessageCommandHandler(db, TestData.Clock);

        var result = await handler.Handle(new SendMessageCommand
        {
            ErrandId = errand.Id,
            SenderId = runnerId,
            Content = "Call me on 0801 234 5678 please"
        }, CancellationToken.None);

        Assert.True(result.ContentWasMasked);

        var messages = await testDb.ReadAsync(r => r.Messages.AsNoTracking().ToListAsync());
        var saved = Assert.Single(messages);
        Assert.Equal("Call me on [hidden] please", saved.Content);
        Assert.Equal(runnerId, saved.SenderId);
    }

    [Fact]
    public async Task SendMessage_WithNoPhoneNumber_IsSavedUnchanged()
    {
        var testDb = new TestDatabase();
        var customerId = Guid.NewGuid();
        var errand = TestData.AcceptedErrand(customerId, Guid.NewGuid());
        await testDb.SeedAsync(s => s.Errands.Add(errand));

        using var db = testDb.NewContext();
        var handler = new SendMessageCommandHandler(db, TestData.Clock);

        var result = await handler.Handle(new SendMessageCommand
        {
            ErrandId = errand.Id,
            SenderId = customerId,
            Content = "Please get 12 items, budget is 4800 naira"
        }, CancellationToken.None);

        Assert.False(result.ContentWasMasked);
        var messages = await testDb.ReadAsync(r => r.Messages.AsNoTracking().ToListAsync());
        Assert.Equal("Please get 12 items, budget is 4800 naira", Assert.Single(messages).Content);
    }

    [Fact]
    public async Task SendMessage_FromSomeoneNotOnTheErrand_IsRefusedAndNothingIsSaved()
    {
        var testDb = new TestDatabase();
        var errand = TestData.AcceptedErrand(Guid.NewGuid(), Guid.NewGuid());
        await testDb.SeedAsync(s => s.Errands.Add(errand));

        using var db = testDb.NewContext();
        var handler = new SendMessageCommandHandler(db, TestData.Clock);

        await Assert.ThrowsAnyAsync<Exception>(() =>
            handler.Handle(new SendMessageCommand
            {
                ErrandId = errand.Id,
                SenderId = Guid.NewGuid(),
                Content = "hello"
            }, CancellationToken.None));

        var messages = await testDb.ReadAsync(r => r.Messages.AsNoTracking().ToListAsync());
        Assert.Empty(messages);
    }
}