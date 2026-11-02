using Errandy.Application.Common.DTOs;
using Errandy.Application.Common.Interfaces;
using Errandy.Application.Common.Results;
using Errandy.Application.Interfaces;
using Errandy.Domain.Entities;
using Errandy.Domain.Enums;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using TransactionEntity = Errandy.Domain.Entities.Transaction;
using WalletTransactionStatus = Errandy.Domain.Enums.TransactionStatus;

namespace Errandy.Application.Features.Wallets.Commands.AdminAdjustWallet;

public record AdminAdjustWalletCommand(
    Guid TargetUserId,
    TransactionType AdjustmentType,
    decimal Amount,
    string Reason,
    bool IsPenalty = false) : IRequest<Result<TransactionDto>>;

public class AdminAdjustWalletCommandValidator : AbstractValidator<AdminAdjustWalletCommand>
{
    public AdminAdjustWalletCommandValidator()
    {
        RuleFor(x => x.TargetUserId)
            .NotEmpty().WithMessage("TargetUserId is required.");

        RuleFor(x => x.Amount)
            .GreaterThan(0).WithMessage("Adjustment amount must be greater than zero.");

        RuleFor(x => x.Reason)
            .NotEmpty().WithMessage("An audit reason is required for admin wallet adjustments.")
            .MinimumLength(5).WithMessage("Audit reason must be at least 5 characters.")
            .MaximumLength(400);
    }
}

public class AdminAdjustWalletCommandHandler
    : IRequestHandler<AdminAdjustWalletCommand, Result<TransactionDto>>
{
    private readonly IApplicationDbContext _db;
    private readonly ICurrentUser _currentUser;

    public AdminAdjustWalletCommandHandler(
        IApplicationDbContext db,
        ICurrentUser currentUser)
    {
        _db = db;
        _currentUser = currentUser;
    }

    public async Task<Result<TransactionDto>> Handle(
        AdminAdjustWalletCommand request,
        CancellationToken cancellationToken)
    {
        if (!_currentUser.IsAuthenticated || _currentUser.UserId is null)
            return Result<TransactionDto>.Failure("User is not authenticated.", ErrorType.Unauthorized);

        var adminId = _currentUser.UserId.Value;

        var adminRole = await _db.Users
            .AsNoTracking()
            .Where(u => u.Id == adminId)
            .Select(u => (UserRole?)u.Role)
            .FirstOrDefaultAsync(cancellationToken);

        if (adminRole != UserRole.Admin)
            return Result<TransactionDto>.Failure(
                "Only administrators can perform manual wallet adjustments.", ErrorType.Forbidden);

        var targetUserExists = await _db.Users
            .AsNoTracking()
            .AnyAsync(u => u.Id == request.TargetUserId, cancellationToken);

        if (!targetUserExists)
            return Result<TransactionDto>.Failure(
                $"Target user '{request.TargetUserId}' was not found.", ErrorType.NotFound);

        var wallet = await _db.Wallets
            .FirstOrDefaultAsync(w => w.UserId == request.TargetUserId, cancellationToken);

        if (wallet is null)
        {
            wallet = new Wallet
            {
                UserId = request.TargetUserId,
                Balance = 0m,
                LockedBalance = 0m,
                Currency = "NGN",
                IsActive = true
            };
            await _db.Wallets.AddAsync(wallet, cancellationToken);
        }

        var roundedAmount = decimal.Round(request.Amount, 2, MidpointRounding.ToEven);
        var balanceBefore = wallet.Balance;

        if (request.AdjustmentType == TransactionType.Credit)
        {
            wallet.AdminCredit(roundedAmount);
        }
        else
        {
            if (wallet.Balance < roundedAmount)
            {
                return Result<TransactionDto>.Failure(
                    $"Target wallet has insufficient available balance ({wallet.Balance:N2} NGN) for a {roundedAmount:N2} NGN debit.",
                    ErrorType.Validation);
            }

            wallet.AdminDebit(roundedAmount);
        }

        var category = request.AdjustmentType == TransactionType.Debit && request.IsPenalty
            ? TransactionCategory.Penalty
            : TransactionCategory.AdminAdjustment;

        var tx = new TransactionEntity
        {
            WalletId = wallet.Id,
            Type = request.AdjustmentType,
            Category = category,
            Amount = roundedAmount,
            BalanceBefore = balanceBefore,
            BalanceAfter = wallet.Balance,
            Status = WalletTransactionStatus.Completed,
            Reference = TransactionEntity.GenerateReference("ADM"),
            Description = $"Admin {request.AdjustmentType} by {adminId}: {request.Reason.Trim()}"
        };

        await _db.Transactions.AddAsync(tx, cancellationToken);
        await _db.SaveChangesAsync(cancellationToken);

        return Result<TransactionDto>.Success(TransactionDto.FromEntity(tx));
    }
}
