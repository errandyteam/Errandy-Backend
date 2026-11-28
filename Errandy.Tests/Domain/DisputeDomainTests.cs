using Errandy.Domain.Entities;
using Errandy.Domain.Enums;
using Errandy.Domain.Exceptions;
using Errandy.Tests.Support;
using Xunit;

namespace Errandy.Tests.Domain;

public class DisputeDomainTests
{
    private static Dispute NewDispute() =>
        Dispute.Create(Guid.NewGuid(), Guid.NewGuid(), DisputeReason.WrongItem, "Wrong brand",
            new[] { "https://example.com/e1.jpg" }, TestData.Now);

    [Fact]
    public void Reject_ClosesTheDisputeAndKeepsTheTrimmedReason()
    {
        var dispute = NewDispute();
        var adminId = Guid.NewGuid();

        dispute.Reject(adminId, "  No evidence provided  ", TestData.Now);

        Assert.Equal(DisputeStatus.Rejected, dispute.Status);
        Assert.Equal("No evidence provided", dispute.ResolutionNotes);
        Assert.Equal(adminId, dispute.ResolvedByAdminId);
        Assert.Equal(TestData.Now, dispute.ResolvedAt);
        Assert.Equal(DisputeResolutionType.None, dispute.Resolution);
    }

    [Fact]
    public void Reject_TwiceIsRefused()
    {
        var dispute = NewDispute();
        dispute.Reject(Guid.NewGuid(), "First decision", TestData.Now);

        Assert.Throws<InvalidOperationException>(() =>
            dispute.Reject(Guid.NewGuid(), "Second decision", TestData.Now));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Reject_WithoutAReason_IsRefused(string notes)
    {
        var dispute = NewDispute();

        Assert.Throws<ArgumentException>(() => dispute.Reject(Guid.NewGuid(), notes, TestData.Now));
        Assert.Equal(DisputeStatus.Open, dispute.Status);
    }

    [Fact]
    public void AddEvidence_AfterRejection_IsRefused()
    {
        var dispute = NewDispute();
        dispute.Reject(Guid.NewGuid(), "No merit", TestData.Now);

        Assert.Throws<InvalidOperationException>(() => dispute.AddEvidence("https://example.com/late.jpg"));
    }

    [Fact]
    public void ReopenAfterDisputeRejected_WithProof_GoesBackToPendingConfirmation()
    {
        var (errand, _) = TestData.DisputedErrand(Guid.NewGuid(), Guid.NewGuid(), withProof: true);

        errand.ReopenAfterDisputeRejected();

        Assert.Equal(ErrandStatus.PendingConfirmation, errand.Status);
    }

    [Fact]
    public void ReopenAfterDisputeRejected_WithoutProof_GoesBackToInProgress()
    {
        var (errand, _) = TestData.DisputedErrand(Guid.NewGuid(), Guid.NewGuid(), withProof: false);

        errand.ReopenAfterDisputeRejected();

        Assert.Equal(ErrandStatus.InProgress, errand.Status);
    }

    [Fact]
    public void ReopenAfterDisputeRejected_WhenNotDisputed_IsRefused()
    {
        var errand = TestData.InProgressErrand(Guid.NewGuid(), Guid.NewGuid());

        Assert.Throws<InvalidErrandStateException>(() => errand.ReopenAfterDisputeRejected());
    }

    [Fact]
    public void RestartConfirmationWindow_StartsTheWindowAgainFromTheGivenTime()
    {
        var errand = TestData.PendingConfirmationErrand(Guid.NewGuid(), Guid.NewGuid());
        var later = TestData.Now.AddHours(30);

        errand.RestartConfirmationWindow(later);

        Assert.Equal(later, errand.PendingConfirmationAt);
        Assert.Equal(ErrandStatus.PendingConfirmation, errand.Status);
    }

    [Fact]
    public void RestartConfirmationWindow_WhenNotWaitingForConfirmation_IsRefused()
    {
        var errand = TestData.InProgressErrand(Guid.NewGuid(), Guid.NewGuid());

        Assert.Throws<InvalidErrandStateException>(() => errand.RestartConfirmationWindow(TestData.Now));
    }
}