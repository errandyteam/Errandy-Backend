using Errandy.Domain.Entities;
using Errandy.Domain.Enums;
using System;

namespace Errandy.Application.Common.Interfaces;

public interface IKycVerificationService
{
    Task<KycVerificationResult> VerifyAsync(RunnerProfile profile, CancellationToken ct);
}

public record KycVerificationResult(
    bool Success,
    KycStatus Status,
    string? RejectionReason = null)
{
    // Provider verification passed.
    // Final approval is still admin-only.
    public static KycVerificationResult VerifiedForReview() =>
        new(true, KycStatus.Pending);

    public static KycVerificationResult Reject(KycStatus status, string reason) =>
        new(false, status, reason);

    public static KycVerificationResult Reject(string reason) =>
        new(false, KycStatus.Rejected, reason);

    // Optional temporary alias so existing code does not break immediately.
    // Replace usages with VerifiedForReview(), then delete this later.
    [Obsolete("Use VerifiedForReview(). KYC provider success does not mean final admin approval.")]
    public static KycVerificationResult Approved => VerifiedForReview();
}