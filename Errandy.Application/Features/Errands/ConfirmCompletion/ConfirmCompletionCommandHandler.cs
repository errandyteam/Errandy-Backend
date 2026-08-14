using Errandy.Application.Common.Exceptions;
using Errandy.Application.Interfaces;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace Errandy.Application.Features.Errands.ConfirmCompletion;

public class ConfirmCompletionCommandHandler : IRequestHandler<ConfirmCompletionCommand>
{
    private readonly IApplicationDbContext _context;
    private readonly IEscrowService _escrowService;
    private readonly IDateTimeProvider _dateTimeProvider;

    public ConfirmCompletionCommandHandler(
        IApplicationDbContext context,
        IEscrowService escrowService,
        IDateTimeProvider dateTimeProvider)
    {
        _context = context;
        _escrowService = escrowService;
        _dateTimeProvider = dateTimeProvider;
    }

    public async Task Handle(ConfirmCompletionCommand request, CancellationToken cancellationToken)
    {
        var errand = await _context.Errands
            .FirstOrDefaultAsync(e => e.Id == request.ErrandId, cancellationToken);

        if (errand is null)
            throw new NotFoundException(nameof(Domain.Entities.Errand), request.ErrandId);

        if (errand.CustomerId != request.CustomerId)
            throw new ForbiddenAccessException("Only the customer who created this errand can confirm completion.");

        if (errand.RunnerId is null || errand.FinalCost is null)
            throw new InvalidOperationException("Errand is missing RunnerId or FinalCost — proof must be uploaded before confirmation.");

        var utcNow = _dateTimeProvider.UtcNow;

        errand.ConfirmCompletion(utcNow);

        await _context.SaveChangesAsync(cancellationToken);

        await _escrowService.ReleaseFundsAsync(
            errand.Id,
            errand.RunnerId.Value,
            errand.FinalCost.Value,
            cancellationToken);
    }
}