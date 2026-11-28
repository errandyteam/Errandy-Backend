using Errandy.Application.Common.Exceptions;
using Errandy.Application.Interfaces;
using Errandy.Domain.Enums;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Errandy.Application.Features.Disputes.ResolveDispute;

public class ResolveDisputeCommandHandler : IRequestHandler<ResolveDisputeCommand>
{
    private readonly IApplicationDbContext _context;
    private readonly IEscrowService _escrowService;
    private readonly IDateTimeProvider _dateTimeProvider;
    private readonly ILogger<ResolveDisputeCommandHandler> _logger;

    public ResolveDisputeCommandHandler(
        IApplicationDbContext context,
        IEscrowService escrowService,
        IDateTimeProvider dateTimeProvider,
        ILogger<ResolveDisputeCommandHandler> logger)
    {
        _context = context;
        _escrowService = escrowService;
        _dateTimeProvider = dateTimeProvider;
        _logger = logger;
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

        // Step 1: decide the outcome and prepare the payout. Nothing is paid yet.
        bool errandConsideredComplete;
        string payoutDescription;
        Func<CancellationToken, Task> payout;

        switch (request.Resolution)
        {
            case DisputeResolutionType.FullPaymentToRunner:
                {
                    if (errand.RunnerId is null)
                        throw new InvalidOperationException("Cannot release full payment: errand has no assigned runner.");

                    var runnerId = errand.RunnerId.Value;
                    errandConsideredComplete = true;
                    payoutDescription = $"release of {totalAmount} to runner {runnerId}";
                    payout = ct => _escrowService.ReleaseFundsAsync(errand.Id, runnerId, totalAmount, ct);
                    break;
                }

            case DisputeResolutionType.FullRefundToCustomer:
                errandConsideredComplete = false; // nothing was delivered, so treat as cancelled
                payoutDescription = $"refund of {totalAmount} to customer {errand.CustomerId}";
                payout = ct => _escrowService.RefundFundsAsync(errand.Id, errand.CustomerId, totalAmount, ct);
                break;

            case DisputeResolutionType.PartialRefundToCustomer:
            case DisputeResolutionType.PenaltyToRunner:
                {
                    if (request.RefundToCustomerAmount is null || request.ReleaseToRunnerAmount is null)
                        throw new ArgumentException(
                            "Both RefundToCustomerAmount and ReleaseToRunnerAmount are required for a partial resolution.");

                    if (errand.RunnerId is null)
                        throw new InvalidOperationException("Cannot release funds to runner: errand has no assigned runner.");

                    var refund = request.RefundToCustomerAmount.Value;
                    var release = request.ReleaseToRunnerAmount.Value;
                    errandConsideredComplete = true; // some work was done and paid for
                    payoutDescription = $"split of {totalAmount}: refund {refund} to customer {errand.CustomerId}, release {release} to runner {errand.RunnerId}";
                    payout = ct => _escrowService.PartialRefundAsync(errand.Id, errand.CustomerId, refund, release, ct);
                    break;
                }

            default:
                throw new ArgumentException($"Unsupported resolution type: {request.Resolution}");
        }

        // Step 2: record the decision and SAVE it first. If another admin resolved this at the same
        // moment, or the errand changed underneath us, this fails with a conflict and NO money has
        // moved. The dispute itself also refuses a second resolution.
        dispute.Resolve(
            request.AdminId,
            request.Resolution,
            request.ReleaseToRunnerAmount ?? request.RefundToCustomerAmount,
            request.Notes,
            utcNow);
        errand.ResolveDisputeAndClose(errandConsideredComplete, utcNow);

        await _context.SaveChangesAsync(cancellationToken);

        // Step 3: now move the money. None: the decision is saved, so the payout must not be
        // skipped just because the caller disconnected.
        try
        {
            await payout(CancellationToken.None);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "RECONCILE: dispute {DisputeId} on errand {ErrandId} was resolved but the payout FAILED ({Payout}).",
                dispute.Id, errand.Id, payoutDescription);
        }
    }
}