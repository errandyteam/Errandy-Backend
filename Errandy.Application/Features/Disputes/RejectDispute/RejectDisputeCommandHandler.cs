using Errandy.Application.Common.Exceptions;
using Errandy.Application.Interfaces;
using Errandy.Domain.Enums;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace Errandy.Application.Features.Disputes.RejectDispute;

public class RejectDisputeCommandHandler : IRequestHandler<RejectDisputeCommand>
{
    private readonly IApplicationDbContext _context;
    private readonly IDateTimeProvider _dateTimeProvider;

    public RejectDisputeCommandHandler(IApplicationDbContext context, IDateTimeProvider dateTimeProvider)
    {
        _context = context;
        _dateTimeProvider = dateTimeProvider;
    }

    public async Task Handle(RejectDisputeCommand request, CancellationToken cancellationToken)
    {
        var dispute = await _context.Disputes
            .FirstOrDefaultAsync(d => d.Id == request.DisputeId, cancellationToken);

        if (dispute is null)
            throw new NotFoundException(nameof(Domain.Entities.Dispute), request.DisputeId);

        var errand = await _context.Errands
            .FirstOrDefaultAsync(e => e.Id == dispute.ErrandId, cancellationToken);

        if (errand is null)
            throw new NotFoundException(nameof(Domain.Entities.Errand), dispute.ErrandId);

        var utcNow = _dateTimeProvider.UtcNow;

        dispute.Reject(request.AdminId, request.Notes, utcNow);
        errand.ReopenAfterDisputeRejected();

        // The dispute may have run longer than the customer's normal response time. Give the
        // customer the full window again, counted from now. Otherwise the next sweep would
        // auto-release the errand immediately.
        if (errand.Status == ErrandStatus.PendingConfirmation)
            errand.RestartConfirmationWindow(utcNow);

        await _context.SaveChangesAsync(cancellationToken);
    }
}