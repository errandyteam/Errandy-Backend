using Errandy.Application.Common.DTOs;
using Errandy.Application.Common.Interfaces;
using Errandy.Application.Common.Results;
using Errandy.Application.Interfaces;
using Errandy.Domain.Enums;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace Errandy.Application.Features.Wallets.Queries.GetRunnerEarnings;

public record GetRunnerEarningsQuery : IRequest<Result<RunnerEarningsSummaryDto>>;

public class GetRunnerEarningsQueryHandler
    : IRequestHandler<GetRunnerEarningsQuery, Result<RunnerEarningsSummaryDto>>
{
    private readonly IApplicationDbContext _db;
    private readonly ICurrentUser _currentUser;

    public GetRunnerEarningsQueryHandler(
        IApplicationDbContext db,
        ICurrentUser currentUser)
    {
        _db = db;
        _currentUser = currentUser;
    }

    public async Task<Result<RunnerEarningsSummaryDto>> Handle(
        GetRunnerEarningsQuery request,
        CancellationToken cancellationToken)
    {
        if (!_currentUser.IsAuthenticated || _currentUser.UserId is null)
            return Result<RunnerEarningsSummaryDto>.Failure("User is not authenticated.", ErrorType.Unauthorized);

        var userId = _currentUser.UserId.Value;

        var runnerHolds = await _db.EscrowHolds
            .AsNoTracking()
            .Where(h => h.RunnerId == userId &&
                        (h.Status == EscrowHoldStatus.Released || h.Status == EscrowHoldStatus.PartiallyReleased))
            .Select(h => new
            {
                h.Status,
                h.ReleasedToRunnerAmount,
                h.CommissionAmount
            })
            .ToListAsync(cancellationToken);

        var completedCount = runnerHolds.Count(h => h.Status == EscrowHoldStatus.Released);
        var disputedCount = runnerHolds.Count(h => h.Status == EscrowHoldStatus.PartiallyReleased && h.ReleasedToRunnerAmount > 0);
        var netEarned = runnerHolds.Sum(h => h.ReleasedToRunnerAmount);
        var totalCommission = runnerHolds.Sum(h => h.CommissionAmount);
        var grossEarned = netEarned + totalCommission;

        var walletBalance = await _db.Wallets
            .AsNoTracking()
            .Where(w => w.UserId == userId)
            .Select(w => (decimal?)w.Balance)
            .FirstOrDefaultAsync(cancellationToken) ?? 0m;

        var summary = new RunnerEarningsSummaryDto(
            UserId: userId,
            CompletedPayoutCount: completedCount,
            DisputedPayoutCount: disputedCount,
            GrossEarnedAmount: grossEarned,
            TotalCommissionDeducted: totalCommission,
            NetEarnedAmount: netEarned,
            AvailableWalletBalance: walletBalance,
            Currency: "NGN");

        return Result<RunnerEarningsSummaryDto>.Success(summary);
    }
}