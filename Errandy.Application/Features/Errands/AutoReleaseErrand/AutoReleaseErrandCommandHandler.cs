using Errandy.Application.Common.Utils;
using Errandy.Application.Interfaces;
using Errandy.Domain.Enums;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Errandy.Application.Features.Errands.AutoReleaseErrand;

public class AutoReleaseErrandCommandHandler : IRequestHandler<AutoReleaseErrandCommand>
{
    private readonly IApplicationDbContext _context;
    private readonly IEscrowService _escrowService;
    private readonly IDateTimeProvider _dateTimeProvider;
    private readonly ILogger<AutoReleaseErrandCommandHandler> _logger;

    public AutoReleaseErrandCommandHandler(
        IApplicationDbContext context,
        IEscrowService escrowService,
        IDateTimeProvider dateTimeProvider,
        ILogger<AutoReleaseErrandCommandHandler> logger)
    {
        _context = context;
        _escrowService = escrowService;
        _dateTimeProvider = dateTimeProvider;
        _logger = logger;
    }

    public async Task Handle(AutoReleaseErrandCommand request, CancellationToken cancellationToken)
    {
        var errand = await _context.Errands
            .FirstOrDefaultAsync(e => e.Id == request.ErrandId, cancellationToken);

        if (errand is null)
        {
            _logger.LogWarning("AutoRelease for errand {ErrandId} skipped: it no longer exists.", request.ErrandId);
            return;
        }

        // Only an errand still waiting on the customer can be auto-released. Confirmed,
        // disputed and cancelled errands are left alone.
        if (errand.Status != ErrandStatus.PendingConfirmation)
        {
            _logger.LogInformation("AutoRelease for errand {ErrandId} skipped: status is {Status}.", errand.Id, errand.Status);
            return;
        }

        var utcNow = _dateTimeProvider.UtcNow;

        // Checked here as well as in the sweep: the window can be restarted (a rejected
        // dispute) between the sweep's lookup and this command running.
        if (errand.PendingConfirmationAt is null
            || errand.PendingConfirmationAt.Value + ErrandTimings.CustomerConfirmationWindow > utcNow)
        {
            _logger.LogInformation("AutoRelease for errand {ErrandId} skipped: confirmation window not over yet.", errand.Id);
            return;
        }

        if (errand.RunnerId is null || errand.FinalCost is null)
        {
            _logger.LogWarning("AutoRelease for errand {ErrandId} skipped: no runner or final cost.", errand.Id);
            return;
        }

        errand.ConfirmCompletion(utcNow);

        try
        {
            await _context.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            // The customer confirmed (or something else changed this errand) at the same moment.
            // They won the race, so there is nothing for us to do and nothing to pay twice.
            _logger.LogInformation(
                "AutoRelease for errand {ErrandId} skipped: it was changed by someone else at the same moment.", errand.Id);
            return;
        }

        try
        {
            await _escrowService.ReleaseFundsAsync(errand.Id, errand.RunnerId.Value, errand.FinalCost.Value, CancellationToken.None);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // The errand is already marked Completed but the payout failed. No person is waiting
            // on this response, so log everything needed to find and fix it.
            _logger.LogError(ex,
                "RECONCILE: errand {ErrandId} auto-completed but release of {Amount} to runner {RunnerId} FAILED.",
                errand.Id, errand.FinalCost, errand.RunnerId);
            return;
        }

        _logger.LogInformation("AutoRelease completed for errand {ErrandId}.", errand.Id);
    }
}