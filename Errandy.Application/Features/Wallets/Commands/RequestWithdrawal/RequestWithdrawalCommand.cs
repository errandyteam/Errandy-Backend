using Errandy.Application.Common.DTOs;
using Errandy.Application.Common.Interfaces;
using Errandy.Application.Common.Results;
using Errandy.Application.Interfaces;
using Errandy.Domain.Entities;
using Errandy.Domain.Enums;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using TransactionEntity = Errandy.Domain.Entities.Transaction;
using WalletTransactionStatus = Errandy.Domain.Enums.TransactionStatus;

namespace Errandy.Application.Features.Wallets.Commands.RequestWithdrawal;

public record RequestWithdrawalCommand(
    decimal Amount,
    string BankName,
    string AccountNumber,
    string AccountName,
    string? BankCode = null) : IRequest<Result<WithdrawalRequestDto>>;

public class RequestWithdrawalCommandValidator : AbstractValidator<RequestWithdrawalCommand>
{
    public RequestWithdrawalCommandValidator()
    {
        RuleFor(x => x.Amount)
            .GreaterThan(0)
            .WithMessage("Withdrawal amount must be greater than zero.");

        RuleFor(x => x.BankName)
            .NotEmpty().WithMessage("Bank name is required.")
            .MaximumLength(120);

        RuleFor(x => x.AccountNumber)
            .NotEmpty().WithMessage("Account number is required.")
            .Matches(@"^\d{10}$")
            .WithMessage("Account number must be a valid 10-digit Nigerian NUBAN account number.");

        RuleFor(x => x.AccountName)
            .NotEmpty().WithMessage("Account name is required.")
            .MaximumLength(150);
    }
}

public class RequestWithdrawalCommandHandler
    : IRequestHandler<RequestWithdrawalCommand, Result<WithdrawalRequestDto>>
{
    private readonly IApplicationDbContext _db;
    private readonly ICurrentUser _currentUser;
    private readonly IDateTimeProvider _dateTimeProvider;
    private readonly int _holdHours;

    public RequestWithdrawalCommandHandler(
        IApplicationDbContext db,
        ICurrentUser currentUser,
        IDateTimeProvider dateTimeProvider,
        IConfiguration configuration)
    {
        _db = db;
        _currentUser = currentUser;
        _dateTimeProvider = dateTimeProvider;

        _holdHours = int.TryParse(configuration["Withdrawal:HoldHours"], out var parsedHours)
         && parsedHours is >= 0 and <= 168
         ? parsedHours
         : 24;
    }

    public async Task<Result<WithdrawalRequestDto>> Handle(
        RequestWithdrawalCommand request,
        CancellationToken cancellationToken)
    {
        if (!_currentUser.IsAuthenticated || _currentUser.UserId is null)
            return Result<WithdrawalRequestDto>.Failure("User is not authenticated.", ErrorType.Unauthorized);

        var userId = _currentUser.UserId.Value;

        var wallet = await _db.Wallets
            .FirstOrDefaultAsync(w => w.UserId == userId, cancellationToken);

        if (wallet is null)
            return Result<WithdrawalRequestDto>.Failure("Wallet not found.", ErrorType.NotFound);

        if (!wallet.IsActive)
            return Result<WithdrawalRequestDto>.Failure(
                "Your wallet is currently restricted and cannot initiate withdrawals.",
                ErrorType.Forbidden);

        var roundedAmount = decimal.Round(request.Amount, 2, MidpointRounding.ToEven);
        if (wallet.Balance < roundedAmount)
        {
            return Result<WithdrawalRequestDto>.Failure(
                $"Insufficient withdrawable balance. Available: {wallet.Balance:N2} NGN, Requested: {roundedAmount:N2} NGN.",
                ErrorType.Validation);
        }

        var utcNow = _dateTimeProvider.UtcNow;
        var reference = TransactionEntity.GenerateReference("WDR");

        // Immediately debit spendable Balance so funds cannot be double-spent during the 24-hour security hold
        var balanceBefore = wallet.Balance;
        wallet.Debit(roundedAmount);

        var transaction = new TransactionEntity
        {
            WalletId = wallet.Id,
            Type = TransactionType.Debit,
            Category = TransactionCategory.Withdrawal,
            Amount = roundedAmount,
            BalanceBefore = balanceBefore,
            BalanceAfter = wallet.Balance,
            Status = WalletTransactionStatus.Pending,
            Reference = reference,
            Description = $"Withdrawal to {request.BankName.Trim()} ({request.AccountNumber.Trim()}) — 24h security hold"
        };

        await _db.Transactions.AddAsync(transaction, cancellationToken);

        var withdrawal = WithdrawalRequest.Create(
            userId: userId,
            walletId: wallet.Id,
            transactionId: transaction.Id,
            amount: roundedAmount,
            bankName: request.BankName,
            accountNumber: request.AccountNumber,
            accountName: request.AccountName,
            reference: reference,
            requestedAtUtc: utcNow,
            holdDuration: TimeSpan.FromHours(_holdHours),
            bankCode: request.BankCode);

        await _db.WithdrawalRequests.AddAsync(withdrawal, cancellationToken);
        await _db.SaveChangesAsync(cancellationToken);

        return Result<WithdrawalRequestDto>.Success(
            WithdrawalRequestDto.FromEntity(withdrawal, utcNow));
    }
}
