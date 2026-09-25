using Errandy.Domain.Enums;

namespace Errandy.Application.DTOs;

public class ErrandDto
{
    public Guid Id { get; set; }
    public Guid CustomerId { get; set; }
    public Guid? RunnerId { get; set; }
    public ErrandCategory Category { get; set; }
    public string Description { get; set; } = string.Empty;
    public ErrandStatus Status { get; set; }
    public decimal EstimatedCost { get; set; }
    public decimal? FinalCost { get; set; }
    public double PickupLatitude { get; set; }
    public double PickupLongitude { get; set; }
    public DateTime? Deadline { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? AcceptedAt { get; set; }
    public DateTime? StartedAt { get; set; }
    public DateTime? PendingConfirmationAt { get; set; }
    public DateTime? CompletedAt { get; set; }
    public ProofDto? Proof { get; set; }
    public bool IsExactLocation { get; set; }
    public ErrandTimePreference TimePreference { get; set; }
    public bool IsOverdue { get; set; }
}

public class ErrandSummaryDto
{
    public Guid Id { get; set; }
    public ErrandCategory Category { get; set; }
    public string Description { get; set; } = string.Empty;
    public ErrandStatus Status { get; set; }
    public decimal EstimatedCost { get; set; }
    public double PickupLatitude { get; set; }
    public double PickupLongitude { get; set; }
    public double DistanceKm { get; set; }
    public DateTime? Deadline { get; set; }
    public DateTime CreatedAt { get; set; }
}

public class ProofDto
{
    public Guid Id { get; set; }
    public string ImageUrl { get; set; } = string.Empty;
    public string? ReceiptUrl { get; set; }
    public DateTime UploadedAt { get; set; }
}
