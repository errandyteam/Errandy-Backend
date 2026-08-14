using Errandy.Domain.Entities;
using Errandy.Domain.Enums;
using Errandy.Domain.Exceptions;
using Xunit;

namespace Errandy.Tests.Domain;

public class ErrandTests
{
    private static readonly DateTime UtcNow = new(2026, 8, 4, 12, 0, 0, DateTimeKind.Utc);
    private static readonly Guid CustomerId = Guid.NewGuid();
    private static readonly Guid RunnerId = Guid.NewGuid();

    private static Errand CreateTestErrand()
    {
        return Errand.Create(
            customerId: CustomerId,
            category: ErrandCategory.MarketGrocery,
            description: "Buy rice and beans",
            estimatedCost: 5000m,
            pickupLatitude: 6.6018,
            pickupLongitude: 3.3515,
            timePreference: ErrandTimePreference.Asap,
            scheduledDeadline: null,
            utcNow: UtcNow);
    }

    [Fact]
    public void Create_SetsStatusToCreated()
    {
        var errand = CreateTestErrand();
        Assert.Equal(ErrandStatus.Created, errand.Status);
    }

    [Fact]
    public void Create_WithZeroCost_Throws()
    {
        Assert.Throws<ArgumentException>(() =>
            Errand.Create(CustomerId, ErrandCategory.Delivery, "test", 0m, 6.6, 3.3, ErrandTimePreference.Asap, null, UtcNow));
    }

    [Fact]
    public void Accept_FromCreated_SetsRunnerAndStatus()
    {
        var errand = CreateTestErrand();
        errand.Accept(RunnerId, UtcNow);

        Assert.Equal(ErrandStatus.Accepted, errand.Status);
        Assert.Equal(RunnerId, errand.RunnerId);
    }

    [Fact]
    public void Accept_WhenAlreadyAccepted_ThrowsInvalidState()
    {
        var errand = CreateTestErrand();
        errand.Accept(RunnerId, UtcNow);

        var otherRunner = Guid.NewGuid();
        Assert.Throws<InvalidErrandStateException>(() => errand.Accept(otherRunner, UtcNow));
    }

    [Fact]
    public void Start_WithoutAcceptingFirst_ThrowsInvalidState()
    {
        var errand = CreateTestErrand();
        Assert.Throws<InvalidErrandStateException>(() => errand.Start(RunnerId, UtcNow));
    }

    [Fact]
    public void Start_ByWrongRunner_ThrowsUnauthorized()
    {
        var errand = CreateTestErrand();
        errand.Accept(RunnerId, UtcNow);

        var impostor = Guid.NewGuid();
        Assert.Throws<UnauthorizedAccessException>(() => errand.Start(impostor, UtcNow));
    }

    [Fact]
    public void AttachProof_MovesToPendingConfirmation()
    {
        var errand = CreateTestErrand();
        errand.Accept(RunnerId, UtcNow);
        errand.Start(RunnerId, UtcNow);

        var proof = Proof.Create(errand.Id, "https://example.com/img.jpg", null, UtcNow);
        errand.AttachProofAndSubmitForConfirmation(RunnerId, proof, 4800m, UtcNow);

        Assert.Equal(ErrandStatus.PendingConfirmation, errand.Status);
        Assert.Equal(4800m, errand.FinalCost);
    }

    [Fact]
    public void ConfirmCompletion_FromPendingConfirmation_MarksCompleted()
    {
        var errand = CreateTestErrand();
        errand.Accept(RunnerId, UtcNow);
        errand.Start(RunnerId, UtcNow);
        var proof = Proof.Create(errand.Id, "https://example.com/img.jpg", null, UtcNow);
        errand.AttachProofAndSubmitForConfirmation(RunnerId, proof, 4800m, UtcNow);

        errand.ConfirmCompletion(UtcNow);

        Assert.Equal(ErrandStatus.Completed, errand.Status);
        Assert.NotNull(errand.CompletedAt);
    }

    [Fact]
    public void ConfirmCompletion_WithoutProof_ThrowsInvalidState()
    {
        var errand = CreateTestErrand();
        errand.Accept(RunnerId, UtcNow);
        errand.Start(RunnerId, UtcNow);

        // Never uploaded proof — still InProgress, not PendingConfirmation
        Assert.Throws<InvalidErrandStateException>(() => errand.ConfirmCompletion(UtcNow));
    }

    [Fact]
    public void AddMessage_BySomeoneNotOnTheErrand_ThrowsUnauthorized()
    {
        var errand = CreateTestErrand();
        var stranger = Guid.NewGuid();

        Assert.Throws<UnauthorizedAccessException>(() =>
            errand.AddMessage(stranger, "hello", UtcNow));
    }

    [Fact]
    public void AddMessage_ByCustomer_Succeeds()
    {
        var errand = CreateTestErrand();
        var message = errand.AddMessage(CustomerId, "Where are you?", UtcNow);

        Assert.Single(errand.Messages);
        Assert.Equal("Where are you?", message.Content);
    }

    [Fact]
    public void Cancel_WhenAlreadyCompleted_ThrowsInvalidState()
    {
        var errand = CreateTestErrand();
        errand.Accept(RunnerId, UtcNow);
        errand.Start(RunnerId, UtcNow);
        var proof = Proof.Create(errand.Id, "https://example.com/img.jpg", null, UtcNow);
        errand.AttachProofAndSubmitForConfirmation(RunnerId, proof, 4800m, UtcNow);
        errand.ConfirmCompletion(UtcNow);

        Assert.Throws<InvalidErrandStateException>(() => errand.Cancel(UtcNow));
    }
}