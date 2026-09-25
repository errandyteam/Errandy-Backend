using MediatR;

namespace Errandy.Application.Features.Errands.AcceptErrand;

public record AcceptErrandCommand : IRequest
{
    public Guid ErrandId { get; init; }
    public Guid RunnerId { get; init; }
}
