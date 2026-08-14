using Errandy.Domain.Enums;

namespace Errandy.Application.DTOs;

public class MessageDto
{
    public Guid Id { get; set; }
    public Guid ErrandId { get; set; }
    public Guid SenderId { get; set; }
    public string Content { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
}

public class DisputeDto
{
    public Guid Id { get; set; }
    public Guid ErrandId { get; set; }
    public Guid RaisedBy { get; set; }
    public DisputeReason Reason { get; set; }
    public string? Description { get; set; }
    public DisputeStatus Status { get; set; }
    public IReadOnlyCollection<string> EvidenceUrls { get; set; } = Array.Empty<string>();
    public DisputeResolutionType Resolution { get; set; }
    public string? ResolutionNotes { get; set; }
    public decimal? ResolutionAmount { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? ResolvedAt { get; set; }
}
