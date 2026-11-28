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

namespace Errandy.Application.Features.Payments.Queries.GetPaymentReconciliationHistory;

public record GetPaymentReconciliationHistoryQuery(string Reference, int Page = 1, int PageSize = 50)
    : IRequest<Result<PaymentReconciliationHistoryPageDto>>;

public class GetPaymentReconciliationHistoryQueryHandler
    : IRequestHandler<GetPaymentReconciliationHistoryQuery, Result<PaymentReconciliationHistoryPageDto>>
{
    private readonly IApplicationDbContext _db;
    private readonly ICurrentUser _currentUser;

    public GetPaymentReconciliationHistoryQueryHandler(IApplicationDbContext db, ICurrentUser currentUser)
    {
        _db = db;
        _currentUser = currentUser;
    }

    public async Task<Result<PaymentReconciliationHistoryPageDto>> Handle(
        GetPaymentReconciliationHistoryQuery request,
        CancellationToken cancellationToken)
    {
        if (!_currentUser.IsAuthenticated || _currentUser.UserId is null)
            return Result<PaymentReconciliationHistoryPageDto>.Failure("User is not authenticated.", ErrorType.Unauthorized);

        var adminId = _currentUser.UserId.Value;
        var isAdmin = await _db.Users.AsNoTracking()
            .AnyAsync(u => u.Id == adminId && u.Role == UserRole.Admin, cancellationToken);
        if (!isAdmin)
            return Result<PaymentReconciliationHistoryPageDto>.Failure("Only administrators can view reconciliation history.", ErrorType.Forbidden);

        if (string.IsNullOrWhiteSpace(request.Reference) || request.Reference.Trim().Length > 100 ||
            request.Page is < 1 or > 10000 || request.PageSize is < 1 or > 100)
            return Result<PaymentReconciliationHistoryPageDto>.Failure("A valid reference and pagination values are required.", ErrorType.Validation);

        var reference = request.Reference.Trim();
        var intentId = await _db.PaymentIntents.AsNoTracking()
            .Where(p => p.Reference == reference)
            .Select(p => (Guid?)p.Id)
            .FirstOrDefaultAsync(cancellationToken);
        if (intentId is null)
            return Result<PaymentReconciliationHistoryPageDto>.Failure("Payment not found.", ErrorType.NotFound);

        var rows = await _db.PaymentReconciliationAttempts.AsNoTracking()
            .Where(a => a.PaymentIntentId == intentId.Value)
            .OrderByDescending(a => a.RequestedAt)
            .ThenByDescending(a => a.Id)
            .Skip((request.Page - 1) * request.PageSize)
            .Take(request.PageSize + 1)
            .ToListAsync(cancellationToken);

        var hasMore = rows.Count > request.PageSize;
        var items = rows.Take(request.PageSize).Select(a => new PaymentReconciliationHistoryItemDto(
            a.Id, a.PaymentIntentId, a.AdminUserId, a.Reference, a.Reason,
            a.Outcome, a.Outcome.ToString(), a.ResultCode, a.RequestedAt, a.FinishedAt)).ToList();
        return Result<PaymentReconciliationHistoryPageDto>.Success(
            new PaymentReconciliationHistoryPageDto(items, request.Page, request.PageSize, hasMore));
    }
}
