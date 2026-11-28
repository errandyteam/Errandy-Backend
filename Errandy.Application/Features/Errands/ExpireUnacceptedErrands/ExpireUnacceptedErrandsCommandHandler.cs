using Errandy.Application.Interfaces;
using Errandy.Domain.Enums;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Errandy.Application.Features.Errands.ExpireUnacceptedErrands;

public class ExpireUnacceptedErrandsCommandHandler : IRequestHandler<ExpireUnacceptedErrandsCommand, int>
{
    // Limits how much one sweep does, so a large backlog can't run for minutes.
    // Anything left over is picked up by the next sweep.
    private const int BatchSize = 100;

    private readonly IApplicationDbContext _context;
    private readonly IEscrowService _escrowService;
    private readonly IDateTimeProvider _dateTimeProvider;
    private readonly ILogger<ExpireUnacceptedErrandsCommandHandler> _logger;

    public ExpireUnacceptedErrandsCommandHandler(
        IApplicationDbContext context,
        IEscrowService escrowService,
        IDateTimeProvider dateTimeProvider,
        ILogger<ExpireUnacceptedErrandsCommandHandler> logger)
    {
        _context = context;
        _escrowService = escrowService;
        _dateTimeProvider = dateTimeProvider;
        _logger = logger;
    }

    public async Task<int> Handle(ExpireUnacceptedErrandsCommand request, CancellationToken cancellationToken)
    {
        var utcNow = _dateTimeProvider.UtcNow;

        var expiredIds = await _context.Errands
            .Where(e => e.Status == ErrandStatus.Created && e.Deadline != null && e.Deadline < utcNow)
            .OrderBy(e => e.Deadline)
            .Select(e => e.Id)
            .Take(BatchSize)
            .ToListAsync(cancellationToken);

        var expiredCount = 0;

        foreach (var errandId in expiredIds)
        {
            var errand = await _context.Errands
                .FirstOrDefaultAsync(e => e.Id == errandId, cancellationToken);

            // A runner may have accepted it since the list was read. Leave it alone.
            if (errand is null || errand.Status != ErrandStatus.Created)
                continue;

            errand.Cancel(utcNow);

            try
            {
                await _context.SaveChangesAsync(cancellationToken);
            }
            catch (DbUpdateConcurrencyException)
            {
                // Someone (for example a runner accepting) changed this errand at the same moment.
                // Stop this sweep. The next one carries on.
                _logger.LogInformation(
                    "Errand {ErrandId} was changed by someone else during the expiry sweep; ending this sweep early.", errandId);
                break;
            }
            catch (Exception ex)
            {
                // Stop this sweep. The next one retries from a clean state.
                _logger.LogError(ex, "Failed to save expiry of errand {ErrandId}; ending this sweep.", errandId);
                break;
            }

            expiredCount++;

            try
            {
                await _escrowService.RefundFundsAsync(errand.Id, errand.CustomerId, errand.EstimatedCost, CancellationToken.None);
            }
            catch (Exception ex)
            {
                // The errand is cancelled but the refund failed. This needs a manual
                // check, so log it loudly with everything needed to find it.
                _logger.LogError(ex,
                    "RECONCILE: errand {ErrandId} expired but refund of {Amount} to customer {CustomerId} FAILED.",
                    errand.Id, errand.EstimatedCost, errand.CustomerId);
            }
        }

        if (expiredCount > 0)
            _logger.LogInformation("Expired {Count} unaccepted errand(s).", expiredCount);

        return expiredCount;
    }
}