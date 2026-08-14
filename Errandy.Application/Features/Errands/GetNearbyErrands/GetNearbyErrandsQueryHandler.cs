using Errandy.Application.Common.Utils;
using Errandy.Application.DTOs;
using Errandy.Application.Interfaces;
using Errandy.Domain.Enums;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace Errandy.Application.Features.Errands.GetNearbyErrands;

public class GetNearbyErrandsQueryHandler : IRequestHandler<GetNearbyErrandsQuery, List<ErrandSummaryDto>>
{
    private readonly IApplicationDbContext _context;

    public GetNearbyErrandsQueryHandler(IApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<List<ErrandSummaryDto>> Handle(GetNearbyErrandsQuery request, CancellationToken cancellationToken)
    {
        var query = _context.Errands.Where(e => e.Status == ErrandStatus.Created);

        if (request.Category is not null)
            query = query.Where(e => e.Category == request.Category);

        var candidates = await query
            .Select(e => new
            {
                e.Id,
                e.Category,
                e.Description,
                e.Status,
                e.EstimatedCost,
                e.PickupLatitude,
                e.PickupLongitude,
                e.Deadline,
                e.CreatedAt
            })
            .ToListAsync(cancellationToken);

        var results = new List<ErrandSummaryDto>();

        foreach (var e in candidates)
        {
            var distanceKm = GeoDistanceCalculator.DistanceKm(
                request.RunnerLatitude, request.RunnerLongitude,
                e.PickupLatitude, e.PickupLongitude);

            if (distanceKm <= request.RadiusKm)
            {
                var (fuzzedLat, fuzzedLng) = LocationPrivacyHelper.Fuzz(e.PickupLatitude, e.PickupLongitude);

                results.Add(new ErrandSummaryDto
                {
                    Id = e.Id,
                    Category = e.Category,
                    Description = e.Description,
                    Status = e.Status,
                    EstimatedCost = e.EstimatedCost,
                    PickupLatitude = fuzzedLat,
                    PickupLongitude = fuzzedLng,
                    DistanceKm = Math.Round(distanceKm, 2),
                    Deadline = e.Deadline,
                    CreatedAt = e.CreatedAt
                });
            }
        }

        return request.SortBy switch
        {
            NearbyErrandsSortBy.EstimatedCost => results.OrderByDescending(r => r.EstimatedCost).ToList(),
            NearbyErrandsSortBy.Newest => results.OrderByDescending(r => r.CreatedAt).ToList(),
            NearbyErrandsSortBy.Deadline => results.OrderBy(r => r.Deadline ?? DateTime.MaxValue).ToList(),
            _ => results.OrderBy(r => r.DistanceKm).ToList()
        };
    }
}