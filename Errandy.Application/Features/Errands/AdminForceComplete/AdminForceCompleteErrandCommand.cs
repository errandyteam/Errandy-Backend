using MediatR;

namespace Errandy.Application.Features.Errands.AdminForceComplete;

public record AdminForceCompleteErrandCommand : IRequest
{
    public Guid ErrandId { get; init; }
    public Guid AdminId { get; init; }
    public string Reason { get; init; } = string.Empty;
}