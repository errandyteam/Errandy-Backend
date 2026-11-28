using Errandy.Application.Common.Utils;
using Errandy.Application.Interfaces;
using Errandy.Domain.Enums;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace Errandy.Application.Features.Errands.GetErrandsDueForAutoRelease;

public class GetErrandsDueForAutoReleaseQueryHandler : IRequestHandler<GetErrandsDueForAutoReleaseQuery, List<Guid>>
{
    // Anything beyond this is picked up by the next sweep.
    private const int BatchSize = 100;

    private readonly IApplicationDbContext _context;
    private readonly IDateTimeProvider _dateTimeProvider;

    public GetErrandsDueForAutoReleaseQueryHandler(IApplicationDbContext context, IDateTimeProvider dateTimeProvider)
    {
        _context = context;
        _dateTimeProvider = dateTimeProvider;
    }

    public async Task<List<Guid>> Handle(GetErrandsDueForAutoReleaseQuery request, CancellationToken cancellationToken)
    {
        var cutoff = _dateTimeProvider.UtcNow - ErrandTimings.CustomerConfirmationWindow;

        // Only errands still waiting on the customer. Disputed errands are not in this status,
        // so a dispute pauses auto-release.
        return await _context.Errands
            .Where(e => e.Status == ErrandStatus.PendingConfirmation
                        && e.PendingConfirmationAt != null
                        && e.PendingConfirmationAt <= cutoff)
            .OrderBy(e => e.PendingConfirmationAt)
            .Select(e => e.Id)
            .Take(BatchSize)
            .ToListAsync(cancellationToken);
    }
}