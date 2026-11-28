using MediatR;

namespace Errandy.Application.Features.Disputes.AddDisputeEvidence;

public record AddDisputeEvidenceCommand : IRequest
{
    public Guid DisputeId { get; init; }
    public Guid UserId { get; init; }
    public List<string> EvidenceUrls { get; init; } = new();
}