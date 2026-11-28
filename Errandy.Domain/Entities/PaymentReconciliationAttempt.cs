using System;
using Errandy.Domain.Common;
using Errandy.Domain.Enums;

namespace Errandy.Domain.Entities;

/// <summary>
/// Audit record for one manual admin verification request.
/// Started means the final outcome is not yet recorded; it does not mean failure.
/// </summary>
public class PaymentReconciliationAttempt : BaseEntity
{
    public Guid PaymentIntentId { get; private set; }
    public Guid AdminUserId { get; private set; }
    public string Reference { get; private set; } = string.Empty;
    public string Reason { get; private set; } = string.Empty;
    public PaymentReconciliationOutcome Outcome { get; private set; } = PaymentReconciliationOutcome.Started;
    public string? ResultCode { get; private set; }
    public DateTime RequestedAt { get; private set; }
    public DateTime? FinishedAt { get; private set; }

    private PaymentReconciliationAttempt() { }

    public static PaymentReconciliationAttempt Start(
        Guid paymentIntentId,
        Guid adminUserId,
        string reference,
        string reason,
        DateTime requestedAtUtc)
    {
        if (paymentIntentId == Guid.Empty)
            throw new ArgumentException("Payment intent ID is required.", nameof(paymentIntentId));
        if (adminUserId == Guid.Empty)
            throw new ArgumentException("Admin user ID is required.", nameof(adminUserId));
        if (string.IsNullOrWhiteSpace(reference) || reference.Trim().Length > 100)
            throw new ArgumentException("Reference is required and cannot exceed 100 characters.", nameof(reference));
        if (string.IsNullOrWhiteSpace(reason) || reason.Trim().Length > 500)
            throw new ArgumentException("Reason is required and cannot exceed 500 characters.", nameof(reason));
        if (requestedAtUtc.Kind != DateTimeKind.Utc)
            throw new ArgumentException("Requested time must be UTC.", nameof(requestedAtUtc));

        return new PaymentReconciliationAttempt
        {
            PaymentIntentId = paymentIntentId,
            AdminUserId = adminUserId,
            Reference = reference.Trim(),
            Reason = reason.Trim(),
            Outcome = PaymentReconciliationOutcome.Started,
            RequestedAt = requestedAtUtc
        };
    }

    // ResultCode is a short internal classification, never a raw gateway response or secret.
    public void Finish(PaymentReconciliationOutcome outcome, string resultCode, DateTime finishedAtUtc)
    {
        if (Outcome != PaymentReconciliationOutcome.Started)
            throw new InvalidOperationException("This reconciliation attempt is already finalized.");
        if (outcome != PaymentReconciliationOutcome.Succeeded &&
            outcome != PaymentReconciliationOutcome.NotCompleted &&
            outcome != PaymentReconciliationOutcome.Unknown)
            throw new ArgumentOutOfRangeException(nameof(outcome), "A final outcome is required.");
        if (string.IsNullOrWhiteSpace(resultCode) || resultCode.Trim().Length > 100)
            throw new ArgumentException("Result code is required and cannot exceed 100 characters.", nameof(resultCode));
        if (finishedAtUtc.Kind != DateTimeKind.Utc)
            throw new ArgumentException("Finished time must be UTC.", nameof(finishedAtUtc));

        Outcome = outcome;
        ResultCode = resultCode.Trim();
        FinishedAt = finishedAtUtc;
        UpdatedAt = finishedAtUtc;
    }
}
