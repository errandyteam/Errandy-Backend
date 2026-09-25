using Errandy.Application.Common.Exceptions;
using Errandy.Application.Interfaces;
using Errandy.Domain.Enums;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace Errandy.Application.Features.Errands.CancelErrand;

public class CancelErrandCommandHandler : IRequestHandler<CancelErrandCommand>
{
    // PRD 13.1: customer cancellation after acceptance incurs a 10-30% penalty.
    // 20% used as a placeholder midpoint — confirm the real number with the team,
    // ideally make it configurable rather than hardcoded once there's an admin settings story.
    private const decimal CustomerLateCancellationPenaltyRate = 0.20m;

    private readonly IApplicationDbContext _context;
    private readonly IEscrowService _escrowService;
    private readonly IDateTimeProvider _dateTimeProvider;

    public CancelErrandCommandHandler(
        IApplicationDbContext context,
        IEscrowService escrowService,
        IDateTimeProvider dateTimeProvider)
    {
        _context = context;
        _escrowService = escrowService;
        _dateTimeProvider = dateTimeProvider;
    }

    public async Task Handle(CancelErrandCommand request, CancellationToken cancellationToken)
    {
        var errand = await _context.Errands
            .FirstOrDefaultAsync(e => e.Id == request.ErrandId, cancellationToken);

        if (errand is null)
            throw new NotFoundException(nameof(Domain.Entities.Errand), request.ErrandId);

        var utcNow = _dateTimeProvider.UtcNow;
        var isCustomer = request.RequestingUserId == errand.CustomerId;
        var isRunner = request.RequestingUserId == errand.RunnerId;

        if (!isCustomer && !isRunner)
            throw new ForbiddenAccessException("Only the customer or assigned runner may cancel this errand.");

        if (isCustomer)
        {
            await HandleCustomerCancellation(errand, utcNow, cancellationToken);
        }
        else
        {
            await HandleRunnerCancellation(errand, utcNow, cancellationToken);
        }

        await _context.SaveChangesAsync(cancellationToken);
    }

    private async Task HandleCustomerCancellation(Domain.Entities.Errand errand, DateTime utcNow, CancellationToken ct)
    {
        switch (errand.Status)
        {
            case ErrandStatus.Created:
                // PRD 13.1: before acceptance — free, full refund.
                errand.Cancel(utcNow);
                await _escrowService.RefundFundsAsync(errand.Id, errand.CustomerId, errand.EstimatedCost, ct);
                break;

            case ErrandStatus.Accepted:
            case ErrandStatus.InProgress:
                // PRD 13.1: after acceptance — penalty fee applies.
                var penalty = errand.EstimatedCost * CustomerLateCancellationPenaltyRate;
                var refundAmount = errand.EstimatedCost - penalty;
                errand.Cancel(utcNow);
                await _escrowService.RefundFundsAsync(errand.Id, errand.CustomerId, refundAmount, ct);
                // NOTE: the confiscated `penalty` amount's destination (platform
                // revenue? partial runner compensation for their trouble?) is a
                // business decision — flag with Engineer B before this goes live.
                break;

            default:
                throw new InvalidOperationException(
                    $"Cannot cancel an errand in status '{errand.Status}'. Use the dispute system instead if there's a problem with completed/pending work.");
        }
    }

    private async Task HandleRunnerCancellation(Domain.Entities.Errand errand, DateTime utcNow, CancellationToken ct)
    {
        switch (errand.Status)
        {
            case ErrandStatus.Accepted:
                // PRD 13.2: before start — allowed, just reopens the errand.
                errand.UnassignRunner(utcNow);
                // TODO: increment this runner's CancellationRate on RunnerProfile
                // (Engineer A's entity) — coordinate once that exists.
                break;

            case ErrandStatus.InProgress:
                // PRD 13.2: after start — penalized. No proof was ever
                // submitted, so the customer gets a full refund; the runner's
                // reputation takes the hit instead of a money penalty here.
                errand.Cancel(utcNow);
                await _escrowService.RefundFundsAsync(errand.Id, errand.CustomerId, errand.EstimatedCost, ct);
                // TODO: same reputation-tracking hook as above, plus PRD's
                // "frequent cancellations -> restriction/suspension" logic
                // lives on Engineer A's RunnerProfile, not here.
                break;

            default:
                throw new InvalidOperationException($"Cannot cancel an errand in status '{errand.Status}'.");
        }
    }
}