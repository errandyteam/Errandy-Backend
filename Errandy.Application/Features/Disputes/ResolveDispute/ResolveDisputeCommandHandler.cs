using Errandy.Application.Common.Exceptions;
using Errandy.Application.Interfaces;
using Errandy.Domain.Enums;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace Errandy.Application.Features.Disputes.ResolveDispute;

public class ResolveDisputeCommandHandler : IRequestHandler<ResolveDisputeCommand>
{
    private readonly IApplicationDbContext _context;
    private readonly IEscrowService _escrowService;
    private readonly IDateTimeProvider _dateTimeProvider;

    public ResolveDisputeCommandHandler(
        IApplicationDbContext context,
        IEscrowService escrowService,
        IDateTimeProvider dateTimeProvider)
    {
        _context = context;
        _escrowService = escrowService;
        _dateTimeProvider = dateTimeProvider;
    }

    public async Task Handle(ResolveDisputeCommand request, CancellationToken cancellationToken)
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
        var totalAmount = errand.FinalCost ?? errand.EstimatedCost;

        bool errandConsideredComplete;

        switch (request.Resolution)
        {
            case DisputeResolutionType.FullPaymentToRunner:
                if (errand.RunnerId is null)
                    throw new InvalidOperationException("Cannot release full payment — errand has no assigned runner.");

                await _escrowService.ReleaseFundsAsync(errand.Id, errand.RunnerId.Value, totalAmount, cancellationToken);
                errandConsideredComplete = true;
                break;

            case DisputeResolutionType.FullRefundToCustomer:
                await _escrowService.RefundFundsAsync(errand.Id, errand.CustomerId, totalAmount, cancellationToken);
                errandConsideredComplete = false;
                break;

            case DisputeResolutionType.PartialRefundToCustomer:
            case DisputeResolutionType.PenaltyToRunner:
                if (request.RefundToCustomerAmount is null || request.ReleaseToRunnerAmount is null)
                    throw new ArgumentException(
                        "Both RefundToCustomerAmount and ReleaseToRunnerAmount are required for a partial resolution.");

                if (errand.RunnerId is null)
                    throw new InvalidOperationException("Cannot release funds to runner — errand has no assigned runner.");

                await _escrowService.PartialRefundAsync(
                    errand.Id,
                    errand.CustomerId,
                    request.RefundToCustomerAmount.Value,
                    request.ReleaseToRunnerAmount.Value,
                    cancellationToken);
                errandConsideredComplete = true;
                break;

            default:
                throw new ArgumentException($"Unsupported resolution type: {request.Resolution}");
        }

        dispute.Resolve(request.AdminId, request.Resolution, request.ReleaseToRunnerAmount ?? request.RefundToCustomerAmount, request.Notes, utcNow);
        errand.ResolveDisputeAndClose(errandConsideredComplete, utcNow);

        await _context.SaveChangesAsync(cancellationToken);
    }
}