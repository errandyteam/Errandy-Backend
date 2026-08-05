using Errandy.Application.Common.Exceptions;
using Errandy.Application.DTOs;
using Errandy.Application.Interfaces;
using Errandy.Domain.Enums;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace Errandy.Application.Features.Errands.GetErrandById;

public class GetErrandByIdQueryHandler : IRequestHandler<GetErrandByIdQuery, ErrandDto>
{
    private readonly IApplicationDbContext _context;

    public GetErrandByIdQueryHandler(IApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<ErrandDto> Handle(GetErrandByIdQuery request, CancellationToken cancellationToken)
    {
        var errand = await _context.Errands
            .Include(e => e.Proof)
            .FirstOrDefaultAsync(e => e.Id == request.ErrandId, cancellationToken);

        if (errand is null)
            throw new NotFoundException(nameof(Domain.Entities.Errand), request.ErrandId);

        // Parties (customer/assigned runner) can always view. Anyone else can
        // only view while it's still open in the marketplace (Status == Created)
        // — mirrors what GetNearbyErrands already shows, just with full detail.
        var isParty = request.RequestingUserId == errand.CustomerId || request.RequestingUserId == errand.RunnerId;
        var isOpenForBrowsing = errand.Status == ErrandStatus.Created;

        if (!isParty && !isOpenForBrowsing)
            throw new ForbiddenAccessException("You do not have access to view this errand.");

        return new ErrandDto
        {
            Id = errand.Id,
            CustomerId = errand.CustomerId,
            RunnerId = errand.RunnerId,
            Category = errand.Category,
            Description = errand.Description,
            Status = errand.Status,
            EstimatedCost = errand.EstimatedCost,
            FinalCost = errand.FinalCost,
            PickupLatitude = errand.PickupLatitude,
            PickupLongitude = errand.PickupLongitude,
            Deadline = errand.Deadline,
            CreatedAt = errand.CreatedAt,
            AcceptedAt = errand.AcceptedAt,
            StartedAt = errand.StartedAt,
            PendingConfirmationAt = errand.PendingConfirmationAt,
            CompletedAt = errand.CompletedAt,
            Proof = errand.Proof is null ? null : new ProofDto
            {
                Id = errand.Proof.Id,
                ImageUrl = errand.Proof.ImageUrl,
                ReceiptUrl = errand.Proof.ReceiptUrl,
                UploadedAt = errand.Proof.UploadedAt
            }
        };
    }
}