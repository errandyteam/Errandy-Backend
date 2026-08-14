using Errandy.Application.Common.Exceptions;
using Errandy.Application.Interfaces;
using Errandy.Domain.Entities;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace Errandy.Application.Features.Disputes.CreateDispute;

public class CreateDisputeCommandHandler : IRequestHandler<CreateDisputeCommand, Guid>
{
    private readonly IApplicationDbContext _context;
    private readonly IDateTimeProvider _dateTimeProvider;

    public CreateDisputeCommandHandler(IApplicationDbContext context, IDateTimeProvider dateTimeProvider)
    {
        _context = context;
        _dateTimeProvider = dateTimeProvider;
    }

    public async Task<Guid> Handle(CreateDisputeCommand request, CancellationToken cancellationToken)
    {
        var errand = await _context.Errands
            .FirstOrDefaultAsync(e => e.Id == request.ErrandId, cancellationToken);

        if (errand is null)
            throw new NotFoundException(nameof(Domain.Entities.Errand), request.ErrandId);

        if (request.RaisedBy != errand.CustomerId && request.RaisedBy != errand.RunnerId)
            throw new ForbiddenAccessException("Only the customer or assigned runner on this errand may raise a dispute.");

        var utcNow = _dateTimeProvider.UtcNow;

        var dispute = Dispute.Create(
            errand.Id,
            request.RaisedBy,
            request.Reason,
            request.Description,
            request.EvidenceUrls,
            utcNow);

        // errand.RaiseDispute() enforces the errand is InProgress or
        // PendingConfirmation, and that it doesn't already have an open dispute.
        errand.RaiseDispute(dispute);

        _context.Disputes.Add(dispute);
        await _context.SaveChangesAsync(cancellationToken);

        return dispute.Id;
    }
}
