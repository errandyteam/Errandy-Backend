using Errandy.Application.Common.Exceptions;
using Errandy.Application.DTOs;
using Errandy.Application.Interfaces;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace Errandy.Application.Features.Disputes.GetDisputeById;

public class GetDisputeByIdQueryHandler : IRequestHandler<GetDisputeByIdQuery, DisputeDto>
{
    private readonly IApplicationDbContext _context;

    public GetDisputeByIdQueryHandler(IApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<DisputeDto> Handle(GetDisputeByIdQuery request, CancellationToken cancellationToken)
    {
        var dispute = await _context.Disputes
            .FirstOrDefaultAsync(d => d.Id == request.DisputeId, cancellationToken);

        if (dispute is null)
            throw new NotFoundException(nameof(Domain.Entities.Dispute), request.DisputeId);

        // Admins can see every dispute. Everyone else only if they are on the errand.
        if (!request.IsAdmin)
        {
            var errand = await _context.Errands
                .FirstOrDefaultAsync(e => e.Id == dispute.ErrandId, cancellationToken);

            if (errand is null || (request.RequestingUserId != errand.CustomerId && request.RequestingUserId != errand.RunnerId))
                throw new ForbiddenAccessException("You do not have access to this dispute.");
        }

        return new DisputeDto
        {
            Id = dispute.Id,
            ErrandId = dispute.ErrandId,
            RaisedBy = dispute.RaisedBy,
            Reason = dispute.Reason,
            Description = dispute.Description,
            Status = dispute.Status,
            EvidenceUrls = dispute.EvidenceUrls.ToList(),
            Resolution = dispute.Resolution,
            ResolutionNotes = dispute.ResolutionNotes,
            ResolutionAmount = dispute.ResolutionAmount,
            CreatedAt = dispute.CreatedAt,
            ResolvedAt = dispute.ResolvedAt
        };
    }
}