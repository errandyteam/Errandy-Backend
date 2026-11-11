using Errandy.Application.Common.DTOs;
using Errandy.Application.Common.Interfaces;
using Errandy.Application.Common.Results;
using Errandy.Application.Interfaces;
using Errandy.Domain.Enums;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace Errandy.Application.Features.Escrow.Queries.GetMyEscrowHolds;

public record GetMyEscrowHoldsQuery(
    EscrowHoldStatus? Status = null,
    int Page = 1,
    int PageSize = 20) : IRequest<Result<IReadOnlyList<EscrowHoldDto>>>;

public class GetMyEscrowHoldsQueryHandler
    : IRequestHandler<GetMyEscrowHoldsQuery, Result<IReadOnlyList<EscrowHoldDto>>>
{
    private readonly IApplicationDbContext _db;
    private readonly ICurrentUser _currentUser;

    public GetMyEscrowHoldsQueryHandler(
        IApplicationDbContext db,
        ICurrentUser currentUser)
    {
        _db = db;
        _currentUser = currentUser;
    }

    public async Task<Result<IReadOnlyList<EscrowHoldDto>>> Handle(
        GetMyEscrowHoldsQuery request,
        CancellationToken cancellationToken)
    {
        if (!_currentUser.IsAuthenticated || _currentUser.UserId is null)
            return Result<IReadOnlyList<EscrowHoldDto>>.Failure("User is not authenticated.", ErrorType.Unauthorized);

        var userId = _currentUser.UserId.Value;
        var page = request.Page < 1 ? 1 : request.Page;
        var pageSize = request.PageSize is < 1 or > 100 ? 20 : request.PageSize;

        var query = _db.EscrowHolds
            .AsNoTracking()
            .Where(h => h.CustomerId == userId || h.RunnerId == userId);

        if (request.Status.HasValue)
            query = query.Where(h => h.Status == request.Status.Value);

        var holds = await query
            .OrderByDescending(h => h.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        var dtos = holds
            .Select(h => EscrowHoldDto.FromEntity(h))
            .ToList();

        return Result<IReadOnlyList<EscrowHoldDto>>.Success(dtos);
    }
}