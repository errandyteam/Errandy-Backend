using Errandy.Application.Common.DTOs;
using Errandy.Application.Common.Interfaces;
using Errandy.Application.Common.Results;
using Errandy.Domain.Enums;
using FluentValidation;
using MediatR;
using TransactionEntity = Errandy.Domain.Entities.Transaction;
using WalletTransactionStatus = Errandy.Domain.Enums.TransactionStatus;

namespace Errandy.Application.Features.Wallets.Commands.DebitWallet;

public record DebitWalletCommand(
    decimal Amount,
    TransactionCategory Category = TransactionCategory.Withdrawal,
    string? Reference = null,
    string? Description = null, 
    Guid? ErrandId = null) : IRequest<Result<TransactionDto>>;

public class DebitWalletCommandValidator : AbstractValidator<DebitWalletCommand>
{
    public DebitWalletCommandValidator()
    {
        RuleFor(x => x.Amount)
            .GreaterThan(0)
            .WithMessage("Debit amount must be greater than zero.");
    }
}

public class DebitWalletCommandHandler
    : IRequestHandler<DebitWalletCommand, Result<TransactionDto>>
{
    private readonly IWalletRepository _wallets;
    private readonly ITransactionRepository _transactions;
    private readonly ICurrentUser _currentUser;
    private readonly IUnitOfWork _uow;

    public DebitWalletCommandHandler(
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
        DebitWalletCommand request,
        CancellationToken cancellationToken)
    {
        if (!_currentUser.IsAuthenticated || _currentUser.UserId is null)
            return Result<TransactionDto>.Failure("User is not authenticated.", ErrorType.Unauthorized);

        var userId = _currentUser.UserId.Value;
        var reference = string.IsNullOrWhiteSpace(request.Reference)
            ? TransactionEntity.GenerateReference("DBT")
            : request.Reference.Trim();

        var existingTx = await _transactions.GetByReferenceAsync(reference, cancellationToken);
        if (existingTx is not null)
            return Result<TransactionDto>.Failure(
                $"A transaction with reference '{reference}' already exists.", ErrorType.Conflict);

        var wallet = await _wallets.GetByUserIdAsync(userId, cancellationToken);
        if (wallet is null)
            return Result<TransactionDto>.Failure("Wallet not found.", ErrorType.NotFound);

        if (!wallet.IsActive)
            return Result<TransactionDto>.Failure("Wallet is currently inactive.", ErrorType.Forbidden);

        var roundedAmount = decimal.Round(request.Amount, 2, MidpointRounding.ToEven);
        if (wallet.Balance < roundedAmount)
            return Result<TransactionDto>.Failure(
                $"Insufficient wallet balance. Available: {wallet.Balance:N2} NGN, Required: {roundedAmount:N2} NGN.",
                ErrorType.Validation);

        var balanceBefore = wallet.Balance;
        wallet.Debit(roundedAmount);

        var transaction = new TransactionEntity
        {
            WalletId = wallet.Id,
            ErrandId = request.ErrandId,
            Type = TransactionType.Debit,
            Category = request.Category,
            Amount = roundedAmount,
            BalanceBefore = balanceBefore,
            BalanceAfter = wallet.Balance,
            Status = WalletTransactionStatus.Completed,
            Reference = reference,
            Description = string.IsNullOrWhiteSpace(request.Description)
                ? "Wallet debit"
                : request.Description.Trim()
        };

        await _transactions.AddAsync(transaction, cancellationToken);
        await _uow.SaveChangesAsync(cancellationToken);

        return Result<TransactionDto>.Success(TransactionDto.FromEntity(transaction));
    }
}