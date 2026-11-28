using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Errandy.Application.Common.DTOs;
using Errandy.Application.Common.Interfaces;
using Errandy.Application.Common.Results;
using Errandy.Application.Interfaces;
using Errandy.Domain.Enums;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace Errandy.Application.Features.Payments.Queries.GetAutomaticPaymentReviewQueue;

// UserId is an optional admin filter, not the authenticated caller's identity.
public record GetAutomaticPaymentReviewQueueQuery(int Page = 1, int PageSize = 50, Guid? UserId = null)
    : IRequest<Result<AutomaticPaymentReviewPageDto>>;

public class GetAutomaticPaymentReviewQueueQueryHandler
    : IRequestHandler<GetAutomaticPaymentReviewQueueQuery, Result<AutomaticPaymentReviewPageDto>>
{
    private readonly IApplicationDbContext _db;
    private readonly ICurrentUser _currentUser;

    public GetAutomaticPaymentReviewQueueQueryHandler(IApplicationDbContext db, ICurrentUser currentUser)
    {
        _db = db;
        _currentUser = currentUser;
    }

    public async Task<Result<AutomaticPaymentReviewPageDto>> Handle(
        GetAutomaticPaymentReviewQueueQuery request, CancellationToken cancellationToken)
    {
        if (!_currentUser.IsAuthenticated || _currentUser.UserId is null)
            return Result<AutomaticPaymentReviewPageDto>.Failure("User is not authenticated.", ErrorType.Unauthorized);

        var callerId = _currentUser.UserId.Value;
        var isAdmin = await _db.Users.AsNoTracking()
            .AnyAsync(u => u.Id == callerId && u.Role == UserRole.Admin, cancellationToken);
        if (!isAdmin)
            return Result<AutomaticPaymentReviewPageDto>.Failure("Only administrators can view automatic payment reviews.", ErrorType.Forbidden);

        if (request.Page is < 1 or > 10000 || request.PageSize is < 1 or > 100 || request.UserId == Guid.Empty)
            return Result<AutomaticPaymentReviewPageDto>.Failure("Invalid pagination or user filter.", ErrorType.Validation);

        // Include every payment status, including Succeeded: an uncertain last
        // attempt may race with a successful payment in another flow. Review
        // cases do not establish whether a payment was credited or has failed.
        var userFilter = request.UserId;
        var query = from check in _db.AutomaticPaymentChecks.AsNoTracking()
                    join payment in _db.PaymentIntents.AsNoTracking() on check.PaymentIntentId equals payment.Id
                    where check.Status == AutomaticPaymentCheckStatus.NeedsReview &&
                          (!userFilter.HasValue || payment.UserId == userFilter)
                    orderby check.LastFinishedAt, check.Id
                    select new
                    {
                        CheckId = check.Id,
                        PaymentIntentId = payment.Id,
                        payment.Reference,
                        payment.UserId,
                        payment.Amount,
                        payment.Currency,
                        PaymentStatus = payment.Status,
                        check.AttemptsStarted,
                        check.MaximumAttempts,
                        check.LastResult,
                        check.LastStartedAt,
                        check.LastFinishedAt
                    };

        var rows = await query.Skip((request.Page - 1) * request.PageSize)
            .Take(request.PageSize + 1).ToListAsync(cancellationToken);
        var hasMore = rows.Count > request.PageSize;
        var items = rows.Take(request.PageSize).Select(row => new AutomaticPaymentReviewDto(
            row.CheckId, row.PaymentIntentId, row.Reference, row.UserId, row.Amount, row.Currency,
            row.PaymentStatus, row.PaymentStatus.ToString(), row.AttemptsStarted, row.MaximumAttempts,
            row.LastResult, row.LastResult.ToString(), row.LastStartedAt, row.LastFinishedAt)).ToList();
        return Result<AutomaticPaymentReviewPageDto>.Success(
            new AutomaticPaymentReviewPageDto(items, request.Page, request.PageSize, hasMore));
    }
}
