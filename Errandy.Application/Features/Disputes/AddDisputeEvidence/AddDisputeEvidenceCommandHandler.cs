using Errandy.Application.Common.Exceptions;
using Errandy.Application.Interfaces;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace Errandy.Application.Features.Disputes.AddDisputeEvidence;

public class AddDisputeEvidenceCommandHandler : IRequestHandler<AddDisputeEvidenceCommand>
{
    private readonly IApplicationDbContext _context;

    public AddDisputeEvidenceCommandHandler(IApplicationDbContext context)
    {
        _context = context;
    }

    public async Task Handle(AddDisputeEvidenceCommand request, CancellationToken cancellationToken)
    {
        var dispute = await _context.Disputes
            .FirstOrDefaultAsync(d => d.Id == request.DisputeId, cancellationToken);

        if (dispute is null)
            throw new NotFoundException(nameof(Domain.Entities.Dispute), request.DisputeId);

        var errand = await _context.Errands
            .FirstOrDefaultAsync(e => e.Id == dispute.ErrandId, cancellationToken);

        if (errand is null)
            throw new NotFoundException(nameof(Domain.Entities.Errand), dispute.ErrandId);

        // Both sides can add evidence, so the other party can respond to the claim.
        if (request.UserId != errand.CustomerId && request.UserId != errand.RunnerId)
            throw new ForbiddenAccessException("Only the customer or assigned runner on this errand can add evidence.");

        // Dispute.AddEvidence refuses to change a dispute that is already closed.
        foreach (var url in request.EvidenceUrls)
            dispute.AddEvidence(url);

        await _context.SaveChangesAsync(cancellationToken);
    }
}