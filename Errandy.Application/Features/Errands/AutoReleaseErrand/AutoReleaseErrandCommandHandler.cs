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
            _logger.LogWarning("AutoRelease fired for errand {ErrandId} but it no longer exists.", request.ErrandId);
            return;
        }

        // No-op if the customer already confirmed, disputed, or the errand
        // was otherwise resolved before this delayed job ran.
        if (errand.Status != ErrandStatus.PendingConfirmation)
        {
            _logger.LogInformation(
                "AutoRelease skipped for errand {ErrandId} — status is {Status}, not PendingConfirmation.",
                errand.Id, errand.Status);
            return;
        }

        if (errand.RunnerId is null || errand.FinalCost is null)
            return; // shouldn't happen if it reached PendingConfirmation, but guard anyway

        var utcNow = _dateTimeProvider.UtcNow;
        errand.ConfirmCompletion(utcNow);
        await _context.SaveChangesAsync(cancellationToken);

        await _escrowService.ReleaseFundsAsync(errand.Id, errand.RunnerId.Value, errand.FinalCost.Value, cancellationToken);

        _logger.LogInformation("AutoRelease completed for errand {ErrandId}.", errand.Id);
    }
}