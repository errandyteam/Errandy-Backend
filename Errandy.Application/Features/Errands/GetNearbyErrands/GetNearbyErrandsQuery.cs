using Errandy.Application.DTOs;
using Errandy.Domain.Enums;
using MediatR;

namespace Errandy.Application.Features.Errands.GetNearbyErrands;

public enum NearbyErrandsSortBy
{
    Distance = 0,
    EstimatedCost = 1,
    Newest = 2,
    Deadline = 3
}

public record GetNearbyErrandsQuery : IRequest<List<ErrandSummaryDto>>
{
    public double RunnerLatitude { get; init; }
    public double RunnerLongitude { get; init; }
    public double RadiusKm { get; init; } = 5.0;
    public ErrandCategory? Category { get; init; }
    public NearbyErrandsSortBy SortBy { get; init; } = NearbyErrandsSortBy.Distance;
}