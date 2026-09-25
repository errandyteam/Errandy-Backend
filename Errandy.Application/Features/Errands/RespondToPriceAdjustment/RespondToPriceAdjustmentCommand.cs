using MediatR;

namespace Errandy.Application.Features.Errands.RespondToPriceAdjustment;

public record RespondToPriceAdjustmentCommand : IRequest
{
    public Guid ErrandId { get; init; }
    public Guid CustomerId { get; init; }
    public bool Approve { get; init; }
}