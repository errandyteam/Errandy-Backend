using Errandy.Application.Common.Exceptions;
using Errandy.Application.Interfaces;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Errandy.Application.Features.Errands.ConfirmCompletion;

public class ConfirmCompletionCommandHandler : IRequestHandler<ConfirmCompletionCommand>
{
    private readonly IApplicationDbContext _context;
    private readonly IEscrowService _escrowService;
    private readonly IDateTimeProvider _dateTimeProvider;
    private readonly ILogger<ConfirmCompletionCommandHandler> _logger;

    public ConfirmCompletionCommandHandler(
        IApplicationDbContext context,
        IEscrowService escrowService,
        IDateTimeProvider dateTimeProvider,
        ILogger<ConfirmCompletionCommandHandler> logger)
    {
        _context = context;
        _escrowService = escrowService;
        _dateTimeProvider = dateTimeProvider;
        _logger = logger;
    }

    public async Task Handle(ConfirmCompletionCommand request, CancellationToken cancellationToken)
    {
        var errand = await _context.Errands
            .FirstOrDefaultAsync(e => e.Id == request.ErrandId, cancellationToken);

        if (errand is null)
            throw new NotFoundException(nameof(Domain.Entities.Errand), request.ErrandId);

        // Ownership is an authorization question, not a state-machine rule, so it is checked here.
        if (errand.CustomerId != request.CustomerId)
            throw new ForbiddenAccessException("Only the customer who created this errand can confirm completion.");

        if (errand.RunnerId is null || errand.FinalCost is null)
            throw new InvalidOperationException("Errand is missing RunnerId or FinalCost: proof must be uploaded before confirmation.");

        var runnerId = errand.RunnerId.Value;
        var amount = errand.FinalCost.Value;

        errand.ConfirmCompletion(_dateTimeProvider.UtcNow);

        // Save the new status FIRST. If auto-release (or anything else) changed this errand at
        // the same moment, this fails with a conflict and the runner is not paid twice.
        await _context.SaveChangesAsync(cancellationToken);

        try
        {
            // None: the status is already saved, so the payout must not be skipped just because
            // the customer's connection dropped.
            await _escrowService.ReleaseFundsAsync(errand.Id, runnerId, amount, CancellationToken.None);
        }
        catch (Exception ex)
        {
            // The customer's confirmation stands, and there is nothing they can do about a failed
            // payout. Log everything needed to find it and pay it by hand.
            _logger.LogError(ex,
                "RECONCILE: errand {ErrandId} was confirmed but release of {Amount} to runner {RunnerId} FAILED.",
                errand.Id, amount, runnerId);
        }
    }
}