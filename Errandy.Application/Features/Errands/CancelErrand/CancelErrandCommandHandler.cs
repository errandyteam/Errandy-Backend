using Errandy.Application.Common.Exceptions;
using Errandy.Application.Interfaces;
using Errandy.Domain.Entities;
using Errandy.Domain.Enums;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Errandy.Application.Features.Errands.CancelErrand;

public class CancelErrandCommandHandler : IRequestHandler<CancelErrandCommand>
{
    // PRD 13.1: customer cancellation after acceptance incurs a 10-30% penalty.
    // 20% is a placeholder midpoint. Confirm the real number with the team.
    private const decimal CustomerLateCancellationPenaltyRate = 0.20m;

    private readonly IApplicationDbContext _context;
    private readonly IEscrowService _escrowService;
    private readonly IDateTimeProvider _dateTimeProvider;
    private readonly ILogger<CancelErrandCommandHandler> _logger;

    public CancelErrandCommandHandler(
        IApplicationDbContext context,
        IEscrowService escrowService,
        IDateTimeProvider dateTimeProvider,
        ILogger<CancelErrandCommandHandler> logger)
    {
        _context = context;
        _escrowService = escrowService;
        _dateTimeProvider = dateTimeProvider;
        _logger = logger;
    }

    public async Task Handle(CancelErrandCommand request, CancellationToken cancellationToken)
    {
        var errand = await _context.Errands
            .FirstOrDefaultAsync(e => e.Id == request.ErrandId, cancellationToken);

        if (errand is null)
            throw new NotFoundException(nameof(Errand), request.ErrandId);

        var utcNow = _dateTimeProvider.UtcNow;
        var isCustomer = request.RequestingUserId == errand.CustomerId;
        var isRunner = request.RequestingUserId == errand.RunnerId;

        if (!isCustomer && !isRunner)
            throw new ForbiddenAccessException("Only the customer or assigned runner may cancel this errand.");

        // Changes the errand's status and works out what (if anything) the customer is owed back.
        var refundAmount = isCustomer
            ? CancelAsCustomer(errand, utcNow)
            : CancelAsRunner(errand, utcNow);

        // Save the new status FIRST. If someone else changed this errand at the same moment
        // (for example a runner accepted it), this save fails with a conflict and NO money has moved.
        await _context.SaveChangesAsync(cancellationToken);

        if (refundAmount is not decimal amount || amount <= 0)
            return;

        try
        {
            // None: the status is already saved, so the refund must not be skipped just
            // because the caller disconnected.
            await _escrowService.RefundFundsAsync(errand.Id, errand.CustomerId, amount, CancellationToken.None);
        }
        catch (Exception ex)
        {
            // The errand is cancelled but the refund failed. This needs a manual check.
            _logger.LogError(ex,
                "RECONCILE: errand {ErrandId} was cancelled but refund of {Amount} to customer {CustomerId} FAILED.",
                errand.Id, amount, errand.CustomerId);
        }
    }

    private static decimal CancelAsCustomer(Errand errand, DateTime utcNow)
    {
        switch (errand.Status)
        {
            case ErrandStatus.Created:
                // PRD 13.1: before acceptance is free, with a full refund.
                errand.Cancel(utcNow);
                return errand.EstimatedCost;

            case ErrandStatus.Accepted:
            case ErrandStatus.InProgress:
                // PRD 13.1: after acceptance a penalty fee applies. The confiscated part's
                // destination (platform revenue? runner compensation?) is a business decision
                // to settle with Engineer B before this goes live.
                var penalty = errand.EstimatedCost * CustomerLateCancellationPenaltyRate;
                errand.Cancel(utcNow);
                return errand.EstimatedCost - penalty;

            default:
                throw new InvalidOperationException(
                    $"Cannot cancel an errand in status '{errand.Status}'. Use the dispute system instead if there's a problem with completed or pending work.");
        }
    }

    private static decimal? CancelAsRunner(Errand errand, DateTime utcNow)
    {
        switch (errand.Status)
        {
            case ErrandStatus.Accepted:
                // PRD 13.2: before start is allowed, and it reopens the errand for other runners.
                errand.UnassignRunner(utcNow);
                // TODO: increment this runner's CancellationRate on RunnerProfile (Engineer A's
                // entity). Coordinate once a hook exists.
                return null;

            case ErrandStatus.InProgress:
                // PRD 13.2: after start the runner is penalized. No proof was submitted, so the
                // customer gets a full refund and the runner's reputation takes the hit.
                errand.Cancel(utcNow);
                // TODO: same reputation hook as above, plus "frequent cancellations lead to
                // restriction or suspension", which lives on Engineer A's RunnerProfile.
                return errand.EstimatedCost;

            default:
                throw new InvalidOperationException($"Cannot cancel an errand in status '{errand.Status}'.");
        }
    }
}