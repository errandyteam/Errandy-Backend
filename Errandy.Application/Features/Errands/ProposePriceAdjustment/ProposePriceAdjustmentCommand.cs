using MediatR;

namespace Errandy.Application.Features.Errands.ProposePriceAdjustment;

public record ProposePriceAdjustmentCommand : IRequest
{
    public Guid ErrandId { get; init; }
    public Guid RunnerId { get; init; }
    public decimal NewCost { get; init; }
    public string Reason { get; init; } = string.Empty;
}