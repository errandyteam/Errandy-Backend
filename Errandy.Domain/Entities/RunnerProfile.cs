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
    public void Approve(Guid adminId)
    {
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
