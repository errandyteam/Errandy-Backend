using Errandy.Application.Common.Exceptions;
using Errandy.Application.Interfaces;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Errandy.Application.Features.Errands.RespondToPriceAdjustment;

public class RespondToPriceAdjustmentCommandHandler : IRequestHandler<RespondToPriceAdjustmentCommand>
{
    private readonly IApplicationDbContext _context;
    private readonly IEscrowService _escrowService;
    private readonly IDateTimeProvider _dateTimeProvider;
    private readonly ILogger<RespondToPriceAdjustmentCommandHandler> _logger;

    public RespondToPriceAdjustmentCommandHandler(
        IApplicationDbContext context,
        IEscrowService escrowService,
        IDateTimeProvider dateTimeProvider,
        ILogger<RespondToPriceAdjustmentCommandHandler> logger)
    {
        _context = context;
        _escrowService = escrowService;
        _dateTimeProvider = dateTimeProvider;
        _logger = logger;
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

        if (!request.Approve)
        {
            // No money is involved when a proposal is turned down.
            errand.RejectPriceAdjustment(utcNow);
            await _context.SaveChangesAsync(cancellationToken);
            return;
        }

        var oldAmount = errand.EstimatedCost;
        var newAmount = errand.ProposedCost
            ?? throw new InvalidOperationException("There is no pending price adjustment to approve.");

        // The one place where money moves BEFORE the status is saved. A price rise can fail
        // for a real reason (the customer can't afford it), and that must block the approval
        // instead of leaving a higher price with no money behind it. If the adjustment is
        // refused, nothing has changed and the proposal stays pending. None: once started, the
        // money step must not be cut off halfway by a dropped connection.
        await _escrowService.AdjustLockedFundsAsync(errand.Id, errand.CustomerId, oldAmount, newAmount, CancellationToken.None);

        try
        {
            errand.ApprovePriceAdjustment(utcNow);
            await _context.SaveChangesAsync(cancellationToken);
        }
        catch
        {
            // The money moved but the approval didn't save (for example the errand changed at the
            // same moment). Put the money back so the escrow matches the errand again.
            try
            {
                await _escrowService.AdjustLockedFundsAsync(errand.Id, errand.CustomerId, newAmount, oldAmount, CancellationToken.None);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    "RECONCILE: errand {ErrandId} price adjustment {Old} to {New} moved money but the approval did not save, and putting it back FAILED.",
                    errand.Id, oldAmount, newAmount);
            }

            throw;
        }
    }
}