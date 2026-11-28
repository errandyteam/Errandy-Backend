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

namespace Errandy.Application.Features.Payments.Queries.GetPendingPaymentReviews;

public record GetPendingPaymentReviewsQuery(int Page = 1, int PageSize = 50)
    : IRequest<Result<PendingPaymentReviewPageDto>>;

public class GetPendingPaymentReviewsQueryHandler
    : IRequestHandler<GetPendingPaymentReviewsQuery, Result<PendingPaymentReviewPageDto>>
{
    private readonly IApplicationDbContext _db;
    private readonly ICurrentUser _currentUser;

    public GetPendingPaymentReviewsQueryHandler(IApplicationDbContext db, ICurrentUser currentUser)
    {
        _db = db;
        _currentUser = currentUser;
    }

    public async Task<Result<PendingPaymentReviewPageDto>> Handle(
        GetPendingPaymentReviewsQuery request,
        CancellationToken cancellationToken)
    {
        if (!_currentUser.IsAuthenticated || _currentUser.UserId is null)
            return Result<PendingPaymentReviewPageDto>.Failure("User is not authenticated.", ErrorType.Unauthorized);

        // Check the current role from the database, not just a supplied JWT claim.
        var callerId = _currentUser.UserId.Value;
        var isAdmin = await _db.Users.AsNoTracking()
            .AnyAsync(u => u.Id == callerId && u.Role == UserRole.Admin, cancellationToken);
        if (!isAdmin)
            return Result<PendingPaymentReviewPageDto>.Failure("Only administrators can review pending payments.", ErrorType.Forbidden);

        // Bounding both values avoids overflow and unbounded database queries.
        if (request.Page is < 1 or > 10000 || request.PageSize is < 1 or > 100)
            return Result<PendingPaymentReviewPageDto>.Failure(
                "Page must be between 1 and 10,000; page size must be between 1 and 100.",
                ErrorType.Validation);

        var rows = await _db.PaymentIntents.AsNoTracking()
            .Where(p => p.Status == PaymentStatus.Pending)
            .OrderBy(p => p.CreatedAt)
            .ThenBy(p => p.Id)
            .Skip((request.Page - 1) * request.PageSize)
            .Take(request.PageSize + 1)
            .Select(p => new PendingPaymentReviewDto(
                p.Id,
                p.Reference,
                p.UserId,
                p.WalletId,
                p.Amount,
                p.Currency,
                p.Provider,
                p.CreatedAt,
                p.AuthorizationUrl != null))
            .ToListAsync(cancellationToken);

        var hasMore = rows.Count > request.PageSize;
        var items = rows.Take(request.PageSize).ToList();
        return Result<PendingPaymentReviewPageDto>.Success(
            new PendingPaymentReviewPageDto(items, request.Page, request.PageSize, hasMore));
    }
}
