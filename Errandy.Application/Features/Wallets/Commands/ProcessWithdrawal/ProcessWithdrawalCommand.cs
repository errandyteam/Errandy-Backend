using Errandy.Application.Common.DTOs;
using Errandy.Application.Common.Interfaces;
using Errandy.Application.Common.Results;
using Errandy.Application.Interfaces;
using Errandy.Domain.Enums;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using TransactionEntity = Errandy.Domain.Entities.Transaction;
using WalletTransactionStatus = Errandy.Domain.Enums.TransactionStatus;

namespace Errandy.Application.Features.Wallets.Commands.ProcessWithdrawal;

public record ProcessWithdrawalCommand(
    Guid WithdrawalId,
    bool Approve,
    string? Note = null,
    bool ForceBypassHold = false) : IRequest<Result<WithdrawalRequestDto>>;

public class ProcessWithdrawalCommandValidator : AbstractValidator<ProcessWithdrawalCommand>
{
    public ProcessWithdrawalCommandValidator()
    {
        RuleFor(x => x.Note)
            .NotEmpty()
            .When(x => !x.Approve)
            .WithMessage("A reason note is required when rejecting a withdrawal.");
    }
}

public class ProcessWithdrawalCommandHandler
    : IRequestHandler<ProcessWithdrawalCommand, Result<WithdrawalRequestDto>>
{
    private readonly IApplicationDbContext _db;
    private readonly ICurrentUser _currentUser;
    private readonly IDateTimeProvider _dateTimeProvider;

    public ProcessWithdrawalCommandHandler(
        IApplicationDbContext db,
        ICurrentUser currentUser,
        IDateTimeProvider dateTimeProvider)
    {
        _db = db;
        _currentUser = currentUser;
        _dateTimeProvider = dateTimeProvider;
    }

    public async Task<Result<WithdrawalRequestDto>> Handle(
        ProcessWithdrawalCommand request,
        CancellationToken cancellationToken)
    {
        if (!_currentUser.IsAuthenticated || _currentUser.UserId is null)
            return Result<WithdrawalRequestDto>.Failure("User is not authenticated.", ErrorType.Unauthorized);

        var adminId = _currentUser.UserId.Value;

        var adminRole = await _db.Users
            .AsNoTracking()
            .Where(u => u.Id == adminId)
            .Select(u => (UserRole?)u.Role)
            .FirstOrDefaultAsync(cancellationToken);

        if (adminRole != UserRole.Admin)
            return Result<WithdrawalRequestDto>.Failure(
                "Only administrators can process withdrawal requests.", ErrorType.Forbidden);

        var withdrawal = await _db.WithdrawalRequests
            .FirstOrDefaultAsync(w => w.Id == request.WithdrawalId, cancellationToken);

        if (withdrawal is null)
            return Result<WithdrawalRequestDto>.Failure("Withdrawal request not found.", ErrorType.NotFound);

        if (withdrawal.Status != WithdrawalStatus.PendingHold)
            return Result<WithdrawalRequestDto>.Failure(
                $"Withdrawal has already been processed ('{withdrawal.Status}').", ErrorType.Validation);

        var utcNow = _dateTimeProvider.UtcNow;

        var originalTx = await _db.Transactions
            .FirstOrDefaultAsync(t => t.Id == withdrawal.TransactionId, cancellationToken);

        if (request.Approve)
        {
            if (!withdrawal.IsMatured(utcNow) && !request.ForceBypassHold)
            {
                return Result<WithdrawalRequestDto>.Failure(
                    $"Withdrawal is still in its 24-hour security hold until {withdrawal.EligibleAt:O}. Pass forceBypassHold=true to override.",
                    ErrorType.Validation);
            }

            withdrawal.Complete(utcNow, adminId, request.Note);

            if (originalTx is not null)
            {
                originalTx.Status = WalletTransactionStatus.Completed;
            }
        }
        else
        {
            var wallet = await _db.Wallets
                .FirstOrDefaultAsync(w => w.Id == withdrawal.WalletId, cancellationToken);

            if (wallet is null)
                return Result<WithdrawalRequestDto>.Failure("Associated wallet not found.", ErrorType.NotFound);

            var balanceBefore = wallet.Balance;
            wallet.RestoreWithheldFunds(withdrawal.Amount);
            withdrawal.Reject(adminId, request.Note ?? "Rejected by administrator.", utcNow);

            if (originalTx is not null)
            {
                originalTx.Status = WalletTransactionStatus.Reversed;
            }

            var refundTx = new TransactionEntity
            {
                WalletId = wallet.Id,
                Type = TransactionType.Credit,
                Category = TransactionCategory.Withdrawal,
                Amount = withdrawal.Amount,
                BalanceBefore = balanceBefore,
                BalanceAfter = wallet.Balance,
                Status = WalletTransactionStatus.Completed,
                Reference = TransactionEntity.GenerateReference("WDR-REJ"),
                Description = $"Refund for rejected withdrawal {withdrawal.Reference}: {withdrawal.AdminNote}"
            };

            await _db.Transactions.AddAsync(refundTx, cancellationToken);
        }

        await _db.SaveChangesAsync(cancellationToken);

        return Result<WithdrawalRequestDto>.Success(
            WithdrawalRequestDto.FromEntity(withdrawal, utcNow));
    }
}
