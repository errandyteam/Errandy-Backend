using MediatR;

namespace Errandy.Application.Features.Disputes.RejectDispute;

public record RejectDisputeCommand : IRequest
{
    public Guid DisputeId { get; init; }
    public Guid AdminId { get; init; }
    public string Notes { get; init; } = string.Empty;
}