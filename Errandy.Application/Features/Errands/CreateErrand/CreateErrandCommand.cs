using Errandy.Domain.Enums;
using MediatR;

namespace Errandy.Application.Features.Errands.CreateErrand;

public record CreateErrandCommand : IRequest<Guid>
{
    public Guid CustomerId { get; init; }
    public ErrandCategory Category { get; init; }
    public string Description { get; init; } = string.Empty;
    public decimal EstimatedCost { get; init; }
    public double PickupLatitude { get; init; }
    public double PickupLongitude { get; init; }
    public DateTime? Deadline { get; init; }
}
