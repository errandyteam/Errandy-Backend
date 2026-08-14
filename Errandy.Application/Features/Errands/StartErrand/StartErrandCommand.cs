using MediatR;

namespace Errandy.Application.Features.Errands.StartErrand;

/// <summary>
/// Runner marks an accepted errand as actively in progress (e.g. they've
/// left to go do the shopping/delivery/task).
/// </summary>
public record StartErrandCommand : IRequest
{
    public Guid ErrandId { get; init; }
    public Guid RunnerId { get; init; }
}
