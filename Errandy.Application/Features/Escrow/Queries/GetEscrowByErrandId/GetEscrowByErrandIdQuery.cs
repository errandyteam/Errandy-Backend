using Errandy.Application.Common.DTOs;
using Errandy.Application.Common.Interfaces;
using Errandy.Application.Common.Results;
using Errandy.Application.Interfaces;
using Errandy.Domain.Enums;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace Errandy.Application.Features.Escrow.Queries.GetEscrowByErrandId;

public record GetEscrowByErrandIdQuery(Guid ErrandId) : IRequest<Result<EscrowHoldDto>>;

public class GetEscrowByErrandIdQueryHandler
    : IRequestHandler<GetEscrowByErrandIdQuery, Result<EscrowHoldDto>>
{
    private readonly IApplicationDbContext _db;
    private readonly ICurrentUser _currentUser;

    public GetEscrowByErrandIdQueryHandler(
        IApplicationDbContext db,
        ICurrentUser currentUser)
    {
        _db = db;
        _currentUser = currentUser;
    }

    public async Task<Result<EscrowHoldDto>> Handle(
        GetEscrowByErrandIdQuery request,
        CancellationToken cancellationToken)
    {
        if (!_currentUser.IsAuthenticated || _currentUser.UserId is null)
            return Result<EscrowHoldDto>.Failure("User is not authenticated.", ErrorType.Unauthorized);

        var userId = _currentUser.UserId.Value;

        var hold = await _db.EscrowHolds
            .AsNoTracking()
            .FirstOrDefaultAsync(h => h.ErrandId == request.ErrandId, cancellationToken);

        if (hold is null)
            return Result<EscrowHoldDto>.Failure(
                $"No escrow hold found for errand '{request.ErrandId}'.", ErrorType.NotFound);

        if (hold.CustomerId != userId && hold.RunnerId != userId)
        {
            var errand = await _db.Errands
                .AsNoTracking()
                .FirstOrDefaultAsync(e => e.Id == request.ErrandId, cancellationToken);

            var userRole = await _db.Users
                .AsNoTracking()
                .Where(u => u.Id == userId)
                .Select(u => (UserRole?)u.Role)
                .FirstOrDefaultAsync(cancellationToken);

            var isAssignedRunner = errand?.RunnerId == userId;
            var isAdmin = userRole == UserRole.Admin;

            if (!isAssignedRunner && !isAdmin)
            {
                return Result<EscrowHoldDto>.Failure(
                    "You are not authorized to view escrow details for this errand.",
                    ErrorType.Forbidden);
            }
        }

        return Result<EscrowHoldDto>.Success(EscrowHoldDto.FromEntity(hold));
    }
}