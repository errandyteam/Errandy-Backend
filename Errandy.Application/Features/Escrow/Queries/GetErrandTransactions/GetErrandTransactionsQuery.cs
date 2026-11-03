using Errandy.Application.Common.DTOs;
using Errandy.Application.Common.Interfaces;
using Errandy.Application.Common.Results;
using Errandy.Application.Interfaces;
using Errandy.Domain.Enums;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace Errandy.Application.Features.Escrow.Queries.GetErrandTransactions;

public record GetErrandTransactionsQuery(Guid ErrandId)
    : IRequest<Result<IReadOnlyList<TransactionDto>>>;

public class GetErrandTransactionsQueryHandler
    : IRequestHandler<GetErrandTransactionsQuery, Result<IReadOnlyList<TransactionDto>>>
{
    private readonly IApplicationDbContext _db;
    private readonly ICurrentUser _currentUser;

    public GetErrandTransactionsQueryHandler(
        IApplicationDbContext db,
        ICurrentUser currentUser)
    {
        _db = db;
        _currentUser = currentUser;
    }

    public async Task<Result<IReadOnlyList<TransactionDto>>> Handle(
        GetErrandTransactionsQuery request,
        CancellationToken cancellationToken)
    {
        if (!_currentUser.IsAuthenticated || _currentUser.UserId is null)
            return Result<IReadOnlyList<TransactionDto>>.Failure("User is not authenticated.", ErrorType.Unauthorized);

        var userId = _currentUser.UserId.Value;

        var userRole = await _db.Users
            .AsNoTracking()
            .Where(u => u.Id == userId)
            .Select(u => (UserRole?)u.Role)
            .FirstOrDefaultAsync(cancellationToken);

        var isAdmin = userRole == UserRole.Admin;

        if (!isAdmin)
        {
            var errand = await _db.Errands
                .AsNoTracking()
                .FirstOrDefaultAsync(e => e.Id == request.ErrandId, cancellationToken);

            var hold = await _db.EscrowHolds
                .AsNoTracking()
                .FirstOrDefaultAsync(h => h.ErrandId == request.ErrandId, cancellationToken);

            if (errand is null && hold is null)
                return Result<IReadOnlyList<TransactionDto>>.Failure(
                    $"Errand '{request.ErrandId}' was not found.", ErrorType.NotFound);

            var isParticipant = errand?.CustomerId == userId
                || errand?.RunnerId == userId
                || hold?.CustomerId == userId
                || hold?.RunnerId == userId;

            if (!isParticipant)
            {
                return Result<IReadOnlyList<TransactionDto>>.Failure(
                    "You are not authorized to view transactions for this errand.",
                    ErrorType.Forbidden);
            }

            // Non-admin participants only see ledger entries on their own wallet (protects other users' wallet balances)
            var myWalletId = await _db.Wallets
                .AsNoTracking()
                .Where(w => w.UserId == userId)
                .Select(w => (Guid?)w.Id)
                .FirstOrDefaultAsync(cancellationToken);

            if (!myWalletId.HasValue)
                return Result<IReadOnlyList<TransactionDto>>.Success(Array.Empty<TransactionDto>());

            var myTransactions = await _db.Transactions
                .AsNoTracking()
                .Where(t => t.ErrandId == request.ErrandId && t.WalletId == myWalletId.Value)
                .OrderBy(t => t.CreatedAt)
                .ToListAsync(cancellationToken);

            return Result<IReadOnlyList<TransactionDto>>.Success(
                myTransactions.Select(tx => TransactionDto.FromEntity(tx)).ToList());
        }

        // Admins see the full multi-wallet audit trail (Customer Lock/Refund + Runner Release + Platform Commission)
        var allTransactions = await _db.Transactions
            .AsNoTracking()
            .Where(t => t.ErrandId == request.ErrandId)
            .OrderBy(t => t.CreatedAt)
            .ToListAsync(cancellationToken);

        return Result<IReadOnlyList<TransactionDto>>.Success(
            allTransactions.Select(tx => TransactionDto.FromEntity(tx)).ToList());
    }
}