using Errandy.Domain.Enums;

namespace Errandy.Application.DTOs;

public class DisputeSummaryDto
{
    public Guid Id { get; set; }
    public Guid ErrandId { get; set; }
    public Guid RaisedBy { get; set; }
    public DisputeReason Reason { get; set; }
    public DisputeStatus Status { get; set; }
    public DateTime CreatedAt { get; set; }
    public double HoursOpen { get; set; }

    /// <summary>True once the dispute has been open longer than the resolution SLA (PRD 12).</summary>
    public bool IsOverSla { get; set; }
}