using Errandy.Application.DTOs;
using Errandy.Application.Interfaces;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace Errandy.Application.Features.Errands.GetMyErrands;

public class GetMyErrandsQueryHandler : IRequestHandler<GetMyErrandsQuery, List<ErrandSummaryDto>>
{
    private readonly IApplicationDbContext _context;

    public GetMyErrandsQueryHandler(IApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<List<ErrandSummaryDto>> Handle(GetMyErrandsQuery request, CancellationToken cancellationToken)
    {
        var query = request.AsCustomer
            ? _context.Errands.Where(e => e.CustomerId == request.UserId)
            : _context.Errands.Where(e => e.RunnerId == request.UserId);

        if (request.StatusFilter is not null)
            query = query.Where(e => e.Status == request.StatusFilter);

        return await query
            .OrderByDescending(e => e.CreatedAt)
            .Select(e => new ErrandSummaryDto
            {
                Id = e.Id,
                Category = e.Category,
                Description = e.Description,
                Status = e.Status,
                EstimatedCost = e.EstimatedCost,
                PickupLatitude = e.PickupLatitude,
                PickupLongitude = e.PickupLongitude,
                DistanceKm = 0, // not meaningful in this context — always 0
                Deadline = e.Deadline,
                CreatedAt = e.CreatedAt
            })
            .ToListAsync(cancellationToken);
    }
}