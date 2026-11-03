using Errandy.Application.Common.DTOs;
using Errandy.Application.Common.Interfaces;
using Errandy.Application.Common.Results;
using Errandy.Application.Interfaces;
using Errandy.Domain.Enums;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace Errandy.Application.Features.Escrow.Queries.GetPlatformFinancialSummary;

public record GetPlatformFinancialSummaryQuery
    : IRequest<Result<PlatformFinancialSummaryDto>>;

public class GetPlatformFinancialSummaryQueryHandler
    : IRequestHandler<GetPlatformFinancialSummaryQuery, Result<PlatformFinancialSummaryDto>>
{
    private readonly IApplicationDbContext _db;
    private readonly ICurrentUser _currentUser;

    public GetPlatformFinancialSummaryQueryHandler(
        IApplicationDbContext db,
        ICurrentUser currentUser)
    {
        _db = db;
        _currentUser = currentUser;
    }

    public async Task<Result<PlatformFinancialSummaryDto>> Handle(
        GetPlatformFinancialSummaryQuery request,
        CancellationToken cancellationToken)
    {
        if (!_currentUser.IsAuthenticated || _currentUser.UserId is null)
            return Result<PlatformFinancialSummaryDto>.Failure("User is not authenticated.", ErrorType.Unauthorized);

        var userId = _currentUser.UserId.Value;

        var userRole = await _db.Users
            .AsNoTracking()
            .Where(u => u.Id == userId)
            .Select(u => (UserRole?)u.Role)
            .FirstOrDefaultAsync(cancellationToken);

        if (userRole != UserRole.Admin)
        {
            return Result<PlatformFinancialSummaryDto>.Failure(
                "Only administrators can access the platform financial summary.",
                ErrorType.Forbidden);
        }

        var holds = await _db.EscrowHolds
            .AsNoTracking()
            .Select(h => new
            {
                h.Status,
                h.CurrentLockedAmount,
                h.ReleasedToRunnerAmount,
                h.CommissionAmount,
                h.RefundedToCustomerAmount
            })
            .ToListAsync(cancellationToken);

        var activeEscrowCount = holds.Count(h => h.Status == EscrowHoldStatus.Held);
        var totalActiveLocked = holds
            .Where(h => h.Status == EscrowHoldStatus.Held)
            .Sum(h => h.CurrentLockedAmount);

        var settledCount = holds.Count(h => h.Status != EscrowHoldStatus.Held);
        var disputedCount = holds.Count(h => h.Status == EscrowHoldStatus.PartiallyReleased);

        var totalReleasedToRunners = holds.Sum(h => h.ReleasedToRunnerAmount);
        var totalCommissionEarned = holds.Sum(h => h.CommissionAmount);
        var totalRefundedToCustomers = holds.Sum(h => h.RefundedToCustomerAmount);

        var treasuryBalance = await _db.Wallets
            .AsNoTracking()
            .Where(w => w.UserId == userId)
            .Select(w => (decimal?)w.Balance)
            .FirstOrDefaultAsync(cancellationToken) ?? 0m;

        var summary = new PlatformFinancialSummaryDto(
            ActiveEscrowCount: activeEscrowCount,
            TotalActiveEscrowLocked: totalActiveLocked,
            SettledEscrowCount: settledCount,
            DisputedSettlementCount: disputedCount,
            TotalReleasedToRunners: totalReleasedToRunners,
            TotalCommissionEarned: totalCommissionEarned,
            TotalRefundedToCustomers: totalRefundedToCustomers,
            TreasuryWalletBalance: treasuryBalance,
            Currency: "NGN");

        return Result<PlatformFinancialSummaryDto>.Success(summary);
    }
}