using Errandy.Application.Common.Interfaces;
using Errandy.Application.Common.Results;
using Errandy.Application.Interfaces;
using Errandy.Domain.Enums;
using MediatR;
using Microsoft.EntityFrameworkCore;
using WalletTransactionStatus = Errandy.Domain.Enums.TransactionStatus;

namespace Errandy.Application.Features.Wallets.Commands.ProcessMaturedWithdrawals;

public record MaturedWithdrawalsBatchResultDto(
    int ProcessedCount,
    decimal TotalProcessedAmount,
    DateTime ProcessedAtUtc);

public record ProcessMaturedWithdrawalsCommand
    : IRequest<Result<MaturedWithdrawalsBatchResultDto>>;

public class ProcessMaturedWithdrawalsCommandHandler
    : IRequestHandler<ProcessMaturedWithdrawalsCommand, Result<MaturedWithdrawalsBatchResultDto>>
{
    private readonly IApplicationDbContext _db;
    private readonly ICurrentUser _currentUser;
    private readonly IDateTimeProvider _dateTimeProvider;

    public ProcessMaturedWithdrawalsCommandHandler(
        IApplicationDbContext db,
        ICurrentUser currentUser,
        IDateTimeProvider dateTimeProvider)
    {
        _db = db;
        _currentUser = currentUser;
        _dateTimeProvider = dateTimeProvider;
    }

    public async Task<Result<MaturedWithdrawalsBatchResultDto>> Handle(
        ProcessMaturedWithdrawalsCommand request,
        CancellationToken cancellationToken)
    {
        if (!_currentUser.IsAuthenticated || _currentUser.UserId is null)
            return Result<MaturedWithdrawalsBatchResultDto>.Failure("User is not authenticated.", ErrorType.Unauthorized);

        var adminId = _currentUser.UserId.Value;

        var adminRole = await _db.Users
            .AsNoTracking()
            .Where(u => u.Id == adminId)
            .Select(u => (UserRole?)u.Role)
            .FirstOrDefaultAsync(cancellationToken);

        if (adminRole != UserRole.Admin)
            return Result<MaturedWithdrawalsBatchResultDto>.Failure(
                "Only administrators can trigger matured withdrawal batch processing.",
                ErrorType.Forbidden);

        var utcNow = _dateTimeProvider.UtcNow;

        var matured = await _db.WithdrawalRequests
            .Where(w => w.Status == WithdrawalStatus.PendingHold && w.EligibleAt <= utcNow)
            .ToListAsync(cancellationToken);

        if (matured.Count == 0)
        {
            return Result<MaturedWithdrawalsBatchResultDto>.Success(
                new MaturedWithdrawalsBatchResultDto(0, 0m, utcNow));
        }

        var txIds = matured.Select(w => w.TransactionId).ToList();
        var transactions = await _db.Transactions
            .Where(t => txIds.Contains(t.Id))
            .ToDictionaryAsync(t => t.Id, cancellationToken);

        decimal totalAmount = 0m;
        foreach (var withdrawal in matured)
        {
            withdrawal.Complete(utcNow, adminId, "Processed after 24-hour hold maturity.");
            totalAmount += withdrawal.Amount;

            if (transactions.TryGetValue(withdrawal.TransactionId, out var tx))
            {
                tx.Status = WalletTransactionStatus.Completed;
            }
        }

        await _db.SaveChangesAsync(cancellationToken);

        return Result<MaturedWithdrawalsBatchResultDto>.Success(
            new MaturedWithdrawalsBatchResultDto(matured.Count, totalAmount, utcNow));
    }
}
