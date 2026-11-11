using Errandy.Application.Common.DTOs;
using Errandy.Application.Common.Interfaces;
using Errandy.Application.Common.Results;
using Errandy.Application.Interfaces;
using Errandy.Domain.Enums;
using MediatR;
using Microsoft.EntityFrameworkCore;
using TransactionEntity = Errandy.Domain.Entities.Transaction;
using WalletTransactionStatus = Errandy.Domain.Enums.TransactionStatus;

namespace Errandy.Application.Features.Wallets.Commands.CancelWithdrawal;

public record CancelWithdrawalCommand(Guid WithdrawalId)
    : IRequest<Result<WithdrawalRequestDto>>;

public class CancelWithdrawalCommandHandler
    : IRequestHandler<CancelWithdrawalCommand, Result<WithdrawalRequestDto>>
{
    private readonly IApplicationDbContext _db;
    private readonly ICurrentUser _currentUser;
    private readonly IDateTimeProvider _dateTimeProvider;

    public CancelWithdrawalCommandHandler(
        IApplicationDbContext db,
        ICurrentUser currentUser,
        IDateTimeProvider dateTimeProvider)
    {
        _db = db;
        _currentUser = currentUser;
        _dateTimeProvider = dateTimeProvider;
    }

    public async Task<Result<WithdrawalRequestDto>> Handle(
        CancelWithdrawalCommand request,
        CancellationToken cancellationToken)
    {
        if (!_currentUser.IsAuthenticated || _currentUser.UserId is null)
            return Result<WithdrawalRequestDto>.Failure("User is not authenticated.", ErrorType.Unauthorized);

        var userId = _currentUser.UserId.Value;

        var withdrawal = await _db.WithdrawalRequests
            .FirstOrDefaultAsync(w => w.Id == request.WithdrawalId, cancellationToken);

        if (withdrawal is null)
            return Result<WithdrawalRequestDto>.Failure("Withdrawal request not found.", ErrorType.NotFound);

        if (withdrawal.UserId != userId)
            return Result<WithdrawalRequestDto>.Failure(
                "You can only cancel your own withdrawal requests.", ErrorType.Forbidden);

        if (withdrawal.Status != WithdrawalStatus.PendingHold)
            return Result<WithdrawalRequestDto>.Failure(
                $"Withdrawal cannot be cancelled because it is already '{withdrawal.Status}'.",
                ErrorType.Validation);

        var wallet = await _db.Wallets
            .FirstOrDefaultAsync(w => w.Id == withdrawal.WalletId, cancellationToken);

        if (wallet is null)
            return Result<WithdrawalRequestDto>.Failure("Wallet not found.", ErrorType.NotFound);

        var utcNow = _dateTimeProvider.UtcNow;
        var balanceBefore = wallet.Balance;

        wallet.RestoreWithheldFunds(withdrawal.Amount);
        withdrawal.CancelByUser(utcNow);

        var originalTx = await _db.Transactions
            .FirstOrDefaultAsync(t => t.Id == withdrawal.TransactionId, cancellationToken);

        if (originalTx is not null)
        {
            originalTx.Status = WalletTransactionStatus.Reversed;
        }

        var reversalTx = new TransactionEntity
        {
            WalletId = wallet.Id,
            Type = TransactionType.Credit,
            Category = TransactionCategory.Withdrawal,
            Amount = withdrawal.Amount,
            BalanceBefore = balanceBefore,
            BalanceAfter = wallet.Balance,
            Status = WalletTransactionStatus.Completed,
            Reference = TransactionEntity.GenerateReference("WDR-REV"),
            Description = $"Reversal of cancelled withdrawal {withdrawal.Reference}"
        };

        await _db.Transactions.AddAsync(reversalTx, cancellationToken);
        await _db.SaveChangesAsync(cancellationToken);

        return Result<WithdrawalRequestDto>.Success(
            WithdrawalRequestDto.FromEntity(withdrawal, utcNow));
    }
}
