using Errandy.Application.Common.DTOs;
using Errandy.Application.Common.Interfaces;
using Errandy.Application.Common.Results;
using Errandy.Domain.Entities;
using Errandy.Domain.Enums;
using FluentValidation;
using MediatR;
using TransactionEntity = Errandy.Domain.Entities.Transaction;
using WalletTransactionStatus = Errandy.Domain.Enums.TransactionStatus;

namespace Errandy.Application.Features.Wallets.Commands.FundWallet;

public record FundWalletCommand(
    decimal Amount,
    string? Reference = null,
    string? Description = null) : IRequest<Result<TransactionDto>>;

public class FundWalletCommandValidator : AbstractValidator<FundWalletCommand>
{
    public FundWalletCommandValidator()
    {
        RuleFor(x => x.Amount)
            .GreaterThan(0)
            .WithMessage("Funding amount must be greater than zero.")
            .LessThanOrEqualTo(10_000_000m)
            .WithMessage("Funding amount cannot exceed 10,000,000 NGN per transaction.");
    }
}

public class FundWalletCommandHandler
    : IRequestHandler<FundWalletCommand, Result<TransactionDto>>
{
    private readonly IWalletRepository _wallets;
    private readonly ITransactionRepository _transactions;
    private readonly ICurrentUser _currentUser;
    private readonly IUnitOfWork _uow;

    public FundWalletCommandHandler(
        IWalletRepository wallets,
        ITransactionRepository transactions,
        ICurrentUser currentUser,
        IUnitOfWork uow)
    {
        _wallets = wallets;
        _transactions = transactions;
        _currentUser = currentUser;
        _uow = uow;
    }

    public async Task<Result<TransactionDto>> Handle(
        FundWalletCommand request,
        CancellationToken cancellationToken)
    {
        if (!_currentUser.IsAuthenticated || _currentUser.UserId is null)
            return Result<TransactionDto>.Failure("User is not authenticated.", ErrorType.Unauthorized);

        var userId = _currentUser.UserId.Value;
        var reference = string.IsNullOrWhiteSpace(request.Reference)
            ? TransactionEntity.GenerateReference("FND")
            : request.Reference.Trim();

        // Prevent duplicate transaction reference (idempotency check)
        var existingTx = await _transactions.GetByReferenceAsync(reference, cancellationToken);
        if (existingTx is not null)
            return Result<TransactionDto>.Failure(
                $"A transaction with reference '{reference}' already exists.", ErrorType.Conflict);

        var wallet = await _wallets.GetByUserIdAsync(userId, cancellationToken);
        if (wallet is null)
        {
            wallet = new Wallet
            {
                UserId = userId,
                Balance = 0m,
                LockedBalance = 0m,
                Currency = "NGN",
                IsActive = true
            };
            await _wallets.AddAsync(wallet, cancellationToken);
        }

        if (!wallet.IsActive)
            return Result<TransactionDto>.Failure("Wallet is currently inactive.", ErrorType.Forbidden);

        var balanceBefore = wallet.Balance;
        wallet.Credit(request.Amount);

        var transaction = new TransactionEntity
        {
            WalletId = wallet.Id,
            Type = TransactionType.Credit,
            Category = TransactionCategory.Funding,
            Amount = decimal.Round(request.Amount, 2, MidpointRounding.ToEven),
            BalanceBefore = balanceBefore,
            BalanceAfter = wallet.Balance,
            Status = WalletTransactionStatus.Completed,
            Reference = reference,
            Description = string.IsNullOrWhiteSpace(request.Description)
                ? "Wallet funding"
                : request.Description.Trim()
        };

        await _transactions.AddAsync(transaction, cancellationToken);
        await _uow.SaveChangesAsync(cancellationToken);

        return Result<TransactionDto>.Success(TransactionDto.FromEntity(transaction));
    }
}