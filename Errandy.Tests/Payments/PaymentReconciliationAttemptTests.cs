using System;
using Errandy.Domain.Entities;
using Errandy.Domain.Enums;
using Xunit;

namespace Errandy.Tests.Payments;

public class PaymentReconciliationAttemptTests
{
    private static readonly DateTime StartedAt = new(2026, 1, 2, 3, 4, 5, DateTimeKind.Utc);

    [Fact]
    public void Start_RecordsAdminReasonAndAnUnfinishedOutcome()
    {
        var intentId = Guid.NewGuid();
        var adminId = Guid.NewGuid();
        var attempt = PaymentReconciliationAttempt.Start(intentId, adminId, " PAY-test ", " Customer reported a missing credit ", StartedAt);

        Assert.Equal(intentId, attempt.PaymentIntentId);
        Assert.Equal(adminId, attempt.AdminUserId);
        Assert.Equal("PAY-test", attempt.Reference);
        Assert.Equal("Customer reported a missing credit", attempt.Reason);
        Assert.Equal(PaymentReconciliationOutcome.Started, attempt.Outcome);
        Assert.Equal(StartedAt, attempt.RequestedAt);
        Assert.Null(attempt.FinishedAt);
        Assert.Null(attempt.ResultCode);
    }

    [Theory]
    [InlineData(PaymentReconciliationOutcome.Succeeded)]
    [InlineData(PaymentReconciliationOutcome.NotCompleted)]
    [InlineData(PaymentReconciliationOutcome.Unknown)]
    public void Finish_RecordsOneFinalOutcome(PaymentReconciliationOutcome outcome)
    {
        var attempt = Start();
        var finishedAt = StartedAt.AddMinutes(1);

        attempt.Finish(outcome, " TestCode ", finishedAt);

        Assert.Equal(outcome, attempt.Outcome);
        Assert.Equal("TestCode", attempt.ResultCode);
        Assert.Equal(finishedAt, attempt.FinishedAt);
        Assert.Throws<InvalidOperationException>(() => attempt.Finish(outcome, "Again", finishedAt));
    }

    [Fact]
    public void Start_RejectsInvalidIdentifiersAndText()
    {
        Assert.Throws<ArgumentException>(() => PaymentReconciliationAttempt.Start(Guid.Empty, Guid.NewGuid(), "PAY-test", "reason", StartedAt));
        Assert.Throws<ArgumentException>(() => PaymentReconciliationAttempt.Start(Guid.NewGuid(), Guid.Empty, "PAY-test", "reason", StartedAt));
        Assert.Throws<ArgumentException>(() => PaymentReconciliationAttempt.Start(Guid.NewGuid(), Guid.NewGuid(), " ", "reason", StartedAt));
        Assert.Throws<ArgumentException>(() => PaymentReconciliationAttempt.Start(Guid.NewGuid(), Guid.NewGuid(), "PAY-test", " ", StartedAt));
        Assert.Throws<ArgumentException>(() => PaymentReconciliationAttempt.Start(Guid.NewGuid(), Guid.NewGuid(), "PAY-test", new string('a', 501), StartedAt));
        Assert.Throws<ArgumentException>(() => PaymentReconciliationAttempt.Start(Guid.NewGuid(), Guid.NewGuid(), "PAY-test", "reason", DateTime.SpecifyKind(StartedAt, DateTimeKind.Unspecified)));
    }

    [Fact]
    public void Finish_RejectsNonfinalOutcomeAndInvalidDetails()
    {
        var attempt = Start();
        Assert.Throws<ArgumentOutOfRangeException>(() => attempt.Finish(PaymentReconciliationOutcome.Started, "test", StartedAt));
        Assert.Throws<ArgumentException>(() => attempt.Finish(PaymentReconciliationOutcome.Unknown, " ", StartedAt));
        Assert.Throws<ArgumentException>(() => attempt.Finish(PaymentReconciliationOutcome.Unknown, new string('x', 101), StartedAt));
        Assert.Throws<ArgumentException>(() => attempt.Finish(PaymentReconciliationOutcome.Unknown, "test", DateTime.SpecifyKind(StartedAt, DateTimeKind.Unspecified)));
        Assert.Equal(PaymentReconciliationOutcome.Started, attempt.Outcome);
    }

    private static PaymentReconciliationAttempt Start() =>
        PaymentReconciliationAttempt.Start(Guid.NewGuid(), Guid.NewGuid(), "PAY-test", "Customer reported a missing credit", StartedAt);
}
