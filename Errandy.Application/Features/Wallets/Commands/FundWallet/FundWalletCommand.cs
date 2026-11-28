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

    public Task<Result<TransactionDto>> Handle(
        FundWalletCommand request,
        CancellationToken cancellationToken)
    {
        // Do not credit wallets using a caller-supplied amount or reference.
        // Wallet funding must pass through verified gateway settlement.
        return Task.FromResult(Result<TransactionDto>.Failure(
            "Direct wallet funding is disabled. Use verified payment funding when it becomes available.",
            ErrorType.Forbidden));
    }
}
