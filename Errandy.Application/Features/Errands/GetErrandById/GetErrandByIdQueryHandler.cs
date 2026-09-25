using Errandy.Application.Common.Exceptions;
using Errandy.Application.Common.Utils;
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

        var isCustomer = request.RequestingUserId == errand.CustomerId;
        var isAssignedRunner = request.RequestingUserId == errand.RunnerId;
        var isOpenForBrowsing = errand.Status == ErrandStatus.Created;

        if (!isCustomer && !isAssignedRunner && !isOpenForBrowsing)
            throw new ForbiddenAccessException("You do not have access to view this errand.");

        // PRD 8: customer always sees their own exact address. The assigned
        // runner sees exact only once they've actually accepted (RunnerId
        // set). Anyone else browsing (only possible while still Created,
        // per the check above) gets the fuzzed approximate location.
        var showExactLocation = isCustomer || isAssignedRunner;

        var (displayLat, displayLng) = showExactLocation
            ? (errand.PickupLatitude, errand.PickupLongitude)
            : LocationPrivacyHelper.Fuzz(errand.PickupLatitude, errand.PickupLongitude);

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
            PickupLatitude = displayLat,
            PickupLongitude = displayLng,
            IsExactLocation = showExactLocation,
            Deadline = errand.Deadline,
            CreatedAt = errand.CreatedAt,
            AcceptedAt = errand.AcceptedAt,
            StartedAt = errand.StartedAt,
            PendingConfirmationAt = errand.PendingConfirmationAt,
            CompletedAt = errand.CompletedAt,
            TimePreference = errand.TimePreference,
            IsOverdue = errand.IsOverdue(DateTime.UtcNow),
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