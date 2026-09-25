using Errandy.Application.Common.Exceptions;
using Errandy.Application.Interfaces;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace Errandy.Application.Features.Errands.RespondToPriceAdjustment;

public class RespondToPriceAdjustmentCommandHandler : IRequestHandler<RespondToPriceAdjustmentCommand>
{
    private readonly IApplicationDbContext _context;
    private readonly IEscrowService _escrowService;
    private readonly IDateTimeProvider _dateTimeProvider;

    public RespondToPriceAdjustmentCommandHandler(
        IApplicationDbContext context,
        IEscrowService escrowService,
        IDateTimeProvider dateTimeProvider)
    {
        _context = context;
        _escrowService = escrowService;
        _dateTimeProvider = dateTimeProvider;
    }

    public async Task Handle(RespondToPriceAdjustmentCommand request, CancellationToken cancellationToken)
    {
        var errand = await _context.Errands
            .FirstOrDefaultAsync(e => e.Id == request.ErrandId, cancellationToken);

        if (errand is null)
            throw new NotFoundException(nameof(Domain.Entities.Errand), request.ErrandId);

        if (errand.CustomerId != request.CustomerId)
            throw new ForbiddenAccessException("Only the customer who created this errand can respond to a price adjustment.");

        var utcNow = _dateTimeProvider.UtcNow;

        if (request.Approve)
        {
            var oldAmount = errand.EstimatedCost;
            var newAmount = errand.ProposedCost ?? oldAmount; // captured before ApprovePriceAdjustment clears it

            errand.ApprovePriceAdjustment(utcNow);
            await _context.SaveChangesAsync(cancellationToken);

            await _escrowService.AdjustLockedFundsAsync(errand.Id, errand.CustomerId, oldAmount, newAmount, cancellationToken);
        }
        else
        {
            errand.RejectPriceAdjustment(utcNow);
            await _context.SaveChangesAsync(cancellationToken);
        }
    }
}