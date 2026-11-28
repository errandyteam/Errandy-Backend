using Errandy.Application.DTOs;
using Errandy.Application.Interfaces;
using Errandy.Domain.Enums;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace Errandy.Application.Features.Disputes.GetOpenDisputes;

public class GetOpenDisputesQueryHandler : IRequestHandler<GetOpenDisputesQuery, List<DisputeSummaryDto>>
{
    // PRD 12: resolution SLA is 12-24 hours. 24 hours is used as the "overdue" line.
    private static readonly TimeSpan ResolutionSla = TimeSpan.FromHours(24);

    private readonly IApplicationDbContext _context;
    private readonly IDateTimeProvider _dateTimeProvider;

    public GetOpenDisputesQueryHandler(IApplicationDbContext context, IDateTimeProvider dateTimeProvider)
    {
        _context = context;
        _dateTimeProvider = dateTimeProvider;
    }

    public async Task<List<DisputeSummaryDto>> Handle(GetOpenDisputesQuery request, CancellationToken cancellationToken)
    {
        var utcNow = _dateTimeProvider.UtcNow;

        var disputes = await _context.Disputes
            .Where(d => d.Status == DisputeStatus.Open || d.Status == DisputeStatus.UnderReview)
            .OrderBy(d => d.CreatedAt)
            .Select(d => new { d.Id, d.ErrandId, d.RaisedBy, d.Reason, d.Status, d.CreatedAt })
            .ToListAsync(cancellationToken);

        return disputes
            .Select(d => new DisputeSummaryDto
            {
                Id = d.Id,
                ErrandId = d.ErrandId,
                RaisedBy = d.RaisedBy,
                Reason = d.Reason,
                Status = d.Status,
                CreatedAt = d.CreatedAt,
                HoursOpen = Math.Round((utcNow - d.CreatedAt).TotalHours, 1),
                IsOverSla = utcNow - d.CreatedAt > ResolutionSla
            })
            .ToList();
    }
}