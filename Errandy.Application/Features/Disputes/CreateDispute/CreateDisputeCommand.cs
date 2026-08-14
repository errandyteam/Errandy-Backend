using Errandy.Domain.Enums;
using MediatR;

namespace Errandy.Application.Features.Disputes.CreateDispute;

/// <summary>
/// Either party raises a dispute on an errand (PRD 12.1 — structured reasons).
/// This moves the errand to Disputed status and holds payment (PRD "Completion
/// System" outcome: Dispute → payment held). No escrow call here — funds were
/// already locked at creation; disputing just prevents release until Admin resolves.
/// </summary>
public record CreateDisputeCommand : IRequest<Guid>
{
    public Guid ErrandId { get; init; }
    public Guid RaisedBy { get; init; }
    public DisputeReason Reason { get; init; }
    public string? Description { get; init; }
    public List<string> EvidenceUrls { get; init; } = new();
}
