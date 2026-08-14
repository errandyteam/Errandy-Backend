using Errandy.Application.Common.Exceptions;
using Errandy.Application.Interfaces;
using Errandy.Domain.Entities;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace Errandy.Application.Features.Errands.UploadProof;

public class UploadProofCommandHandler : IRequestHandler<UploadProofCommand>
{
    private readonly IApplicationDbContext _context;
    private readonly IDateTimeProvider _dateTimeProvider;
    private readonly IBackgroundJobScheduler _jobScheduler;

    public UploadProofCommandHandler(
        IApplicationDbContext context,
        IDateTimeProvider dateTimeProvider,
        IBackgroundJobScheduler jobScheduler)
    {
        _context = context;
        _dateTimeProvider = dateTimeProvider;
        _jobScheduler = jobScheduler;
    }

    public async Task Handle(UploadProofCommand request, CancellationToken cancellationToken)
    {
        var errand = await _context.Errands
            .FirstOrDefaultAsync(e => e.Id == request.ErrandId, cancellationToken);

        if (errand is null)
            throw new NotFoundException(nameof(Domain.Entities.Errand), request.ErrandId);

        var utcNow = _dateTimeProvider.UtcNow;
        var proof = Proof.Create(errand.Id, request.ImageUrl, request.ReceiptUrl, utcNow);

        errand.AttachProofAndSubmitForConfirmation(request.RunnerId, proof, request.FinalCost, utcNow);

        _context.Proofs.Add(proof);
        await _context.SaveChangesAsync(cancellationToken);

        // PRD: customer has 2-6 hours to respond before auto-release fires.
        _jobScheduler.ScheduleAutoReleaseErrand(errand.Id, TimeSpan.FromHours(6));
    }
}