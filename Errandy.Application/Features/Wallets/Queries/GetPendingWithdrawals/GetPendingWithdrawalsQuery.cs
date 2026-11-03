using Errandy.Application.Common.DTOs;
using Errandy.Application.Common.Interfaces;
using Errandy.Application.Common.Results;
using Errandy.Application.Interfaces;
using Errandy.Domain.Enums;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace Errandy.Application.Features.Wallets.Queries.GetPendingWithdrawals;

public record GetPendingWithdrawalsQuery(
    WithdrawalStatus? Status = WithdrawalStatus.PendingHold,
    bool MaturedOnly = false,
    int Page = 1,
    int PageSize = 50) : IRequest<Result<IReadOnlyList<WithdrawalRequestDto>>>;

public class GetPendingWithdrawalsQueryHandler
    : IRequestHandler<GetPendingWithdrawalsQuery, Result<IReadOnlyList<WithdrawalRequestDto>>>
{
    private readonly IApplicationDbContext _db;
    private readonly ICurrentUser _currentUser;
    private readonly IDateTimeProvider _dateTimeProvider;

    public GetPendingWithdrawalsQueryHandler(
        IApplicationDbContext db,
        ICurrentUser currentUser,
        IDateTimeProvider dateTimeProvider)
    {
        _db = db;
        _currentUser = currentUser;
        _dateTimeProvider = dateTimeProvider;
    }

    public async Task<Result<IReadOnlyList<WithdrawalRequestDto>>> Handle(
        GetPendingWithdrawalsQuery request,
        CancellationToken cancellationToken)
    {
        if (!_currentUser.IsAuthenticated || _currentUser.UserId is null)
            return Result<IReadOnlyList<WithdrawalRequestDto>>.Failure("User is not authenticated.", ErrorType.Unauthorized);

        var adminId = _currentUser.UserId.Value;

        var adminRole = await _db.Users
            .AsNoTracking()
            .Where(u => u.Id == adminId)
            .Select(u => (UserRole?)u.Role)
            .FirstOrDefaultAsync(cancellationToken);

        if (adminRole != UserRole.Admin)
            return Result<IReadOnlyList<WithdrawalRequestDto>>.Failure(
                "Only administrators can view platform withdrawal queues.", ErrorType.Forbidden);

        var utcNow = _dateTimeProvider.UtcNow;
        var page = request.Page < 1 ? 1 : request.Page;
        var pageSize = request.PageSize is < 1 or > 200 ? 50 : request.PageSize;

        var query = _db.WithdrawalRequests.AsNoTracking();

        if (request.Status.HasValue)
            query = query.Where(w => w.Status == request.Status.Value);

        if (request.MaturedOnly)
            query = query.Where(w => w.EligibleAt <= utcNow);

        var items = await query
            .OrderBy(w => w.EligibleAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        var dtos = items
            .Select(w => WithdrawalRequestDto.FromEntity(w, utcNow))
            .ToList();

        return Result<IReadOnlyList<WithdrawalRequestDto>>.Success(dtos);
    }
}
