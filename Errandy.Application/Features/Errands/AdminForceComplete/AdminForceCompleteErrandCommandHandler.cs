using Errandy.Application.Common.Exceptions;
using Errandy.Application.Interfaces;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Errandy.Application.Features.Errands.AdminForceComplete;

public class AdminForceCompleteErrandCommandHandler : IRequestHandler<AdminForceCompleteErrandCommand>
{
    private readonly IApplicationDbContext _context;
    private readonly IEscrowService _escrowService;
    private readonly IDateTimeProvider _dateTimeProvider;
    private readonly ILogger<AdminForceCompleteErrandCommandHandler> _logger;

    public AdminForceCompleteErrandCommandHandler(
        IApplicationDbContext context,
        IEscrowService escrowService,
        IDateTimeProvider dateTimeProvider,
        ILogger<AdminForceCompleteErrandCommandHandler> logger)
    {
        _context = context;
        _escrowService = escrowService;
        _dateTimeProvider = dateTimeProvider;
        _logger = logger;
    }

    public async Task Handle(AdminForceCompleteErrandCommand request, CancellationToken cancellationToken)
    {
        var errand = await _context.Errands
            .FirstOrDefaultAsync(e => e.Id == request.ErrandId, cancellationToken);

        if (errand is null)
            throw new NotFoundException(nameof(Domain.Entities.Errand), request.ErrandId);

        if (errand.RunnerId is null)
            throw new InvalidOperationException("Cannot force-complete an errand with no assigned runner.");

        var runnerId = errand.RunnerId.Value;

        errand.AdminForceComplete(_dateTimeProvider.UtcNow);
        await _context.SaveChangesAsync(cancellationToken);

        var amount = errand.FinalCost!.Value;

        // Audit trail: this bypasses the customer's normal approval, so it must always be
        // traceable to who did it and why.
        _logger.LogWarning(
            "Admin {AdminId} force-completed errand {ErrandId}. Reason: {Reason}",
            request.AdminId, errand.Id, request.Reason);

        try
        {
            await _escrowService.ReleaseFundsAsync(errand.Id, runnerId, amount, CancellationToken.None);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "RECONCILE: errand {ErrandId} was force-completed by admin {AdminId} but release of {Amount} to runner {RunnerId} FAILED.",
                errand.Id, request.AdminId, amount, runnerId);
        }
    }
}