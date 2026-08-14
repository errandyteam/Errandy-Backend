using MediatR;

namespace Errandy.Application.Features.Errands.CancelErrand;

public record CancelErrandCommand : IRequest
{
    public Guid ErrandId { get; init; }
    public Guid RequestingUserId { get; init; }
}