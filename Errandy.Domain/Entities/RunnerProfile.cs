using Errandy.Domain.Common;
using Errandy.Domain.Enums;
namespace Errandy.Domain.Entities;
/// <summary>
/// Extended profile that exists for users who are runners.
/// Holds KYC state and performance metrics.
/// </summary>
public class RunnerProfile : BaseEntity
{
    public Guid UserId { get; set; }
    public User User { get; set; } = null!;
    public KycStatus KycStatus { get; set; } = KycStatus.Pending;
    /// <summary>Government ID / document reference submitted during KYC.</summary>
    public string? DocumentType { get; set; }
    public string? DocumentNumber { get; set; }
    public string? DocumentImageUrl { get; set; }
    /// <summary>Reason captured when an admin rejects KYC.</summary>
    public string? RejectionReason { get; set; }
    public DateTime? ReviewedAt { get; set; }
    public Guid? ReviewedByAdminId { get; set; }
 
    public decimal Rating { get; set; } = 0m;

    public decimal CompletionRate { get; set; } = 0m;
 
    public decimal CancellationRate { get; set; } = 0m;

    public RunnerLevel Level { get; set; } = RunnerLevel.Beginner;

    public string? GovernmentIdType { get; set; }
    public string? GovernmentIdNumber { get; set; }
    public string? GovernmentIdImageUrl { get; set; }

    public string? SelfieImageUrl { get; set; }

    public string? BankName { get; set; }
    public string? BankAccountNumber { get; set; }
    public string? BankAccountName { get; set; }
    public string? Bvn { get; set; }

    public VerificationStatus PhoneVerificationStatus { get; set; } = VerificationStatus.NotSubmitted;
    public VerificationStatus GovernmentIdVerificationStatus { get; set; } = VerificationStatus.NotSubmitted;
    public VerificationStatus SelfieVerificationStatus { get; set; } = VerificationStatus.NotSubmitted;
    public VerificationStatus BankVerificationStatus { get; set; } = VerificationStatus.NotSubmitted;

    public bool HasSubmittedDocuments =>
     !string.IsNullOrWhiteSpace(DocumentType) &&
     !string.IsNullOrWhiteSpace(DocumentNumber) &&
     !string.IsNullOrWhiteSpace(DocumentImageUrl);

    public bool IsEligibleToAcceptErrands =>
        KycStatus == KycStatus.Approved &&
        HasSubmittedDocuments;

    public void Approve(Guid adminId)
    {
        if (!HasSubmittedDocuments)
            throw new InvalidOperationException(
                "Cannot approve a runner profile that has not submitted KYC documents.");

        KycStatus = KycStatus.Approved;
        RejectionReason = null;
        ReviewedByAdminId = adminId;
        ReviewedAt = DateTime.UtcNow;
        UpdatedAt = DateTime.UtcNow;
    }
    public void Reject(Guid adminId, string reason)
    {
        KycStatus = KycStatus.Rejected;
        RejectionReason = reason;
        ReviewedByAdminId = adminId;
        ReviewedAt = DateTime.UtcNow;
        UpdatedAt = DateTime.UtcNow;
    }
}
