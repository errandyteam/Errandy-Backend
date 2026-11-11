using Errandy.Application.Common.DTOs;
using Errandy.Application.Common.Interfaces;
using Errandy.Application.Common.Results;
using Errandy.Application.Interfaces;
using Errandy.Domain.Enums;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace Errandy.Application.Features.Wallets.Queries.GetMyWithdrawals;

public record GetMyWithdrawalsQuery(
    WithdrawalStatus? Status = null,
    int Page = 1,
    int PageSize = 20) : IRequest<Result<IReadOnlyList<WithdrawalRequestDto>>>;

public class GetMyWithdrawalsQueryHandler
    : IRequestHandler<GetMyWithdrawalsQuery, Result<IReadOnlyList<WithdrawalRequestDto>>>
{
    private readonly IApplicationDbContext _db;
    private readonly ICurrentUser _currentUser;
    private readonly IDateTimeProvider _dateTimeProvider;

    public GetMyWithdrawalsQueryHandler(
        IApplicationDbContext db,
        ICurrentUser currentUser,
        IDateTimeProvider dateTimeProvider)
    {
        _db = db;
        _currentUser = currentUser;
        _dateTimeProvider = dateTimeProvider;
    }

    public async Task<Result<IReadOnlyList<WithdrawalRequestDto>>> Handle(
        GetMyWithdrawalsQuery request,
        CancellationToken cancellationToken)
    {
        if (!_currentUser.IsAuthenticated || _currentUser.UserId is null)
            return Result<IReadOnlyList<WithdrawalRequestDto>>.Failure("User is not authenticated.", ErrorType.Unauthorized);

        var userId = _currentUser.UserId.Value;
        var page = request.Page < 1 ? 1 : request.Page;
        var pageSize = request.PageSize is < 1 or > 100 ? 20 : request.PageSize;

        var query = _db.WithdrawalRequests
            .AsNoTracking()
            .Where(w => w.UserId == userId);

        if (request.Status.HasValue)
            query = query.Where(w => w.Status == request.Status.Value);

        var items = await query
            .OrderByDescending(w => w.RequestedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        var utcNow = _dateTimeProvider.UtcNow;
        var dtos = items
            .Select(w => WithdrawalRequestDto.FromEntity(w, utcNow))
            .ToList();

        return Result<IReadOnlyList<WithdrawalRequestDto>>.Success(dtos);
    }
}
