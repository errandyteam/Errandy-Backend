using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Errandy.Application.Common.DTOs;
using Errandy.Application.Common.Interfaces;
using Errandy.Application.Common.Results;
using Errandy.Application.Interfaces;
using Errandy.Domain.Entities;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace Errandy.Application.Features.Payments.Commands.InitializePayment;

public record InitializePaymentCommand(decimal Amount) : IRequest<Result<PaymentInitializationDto>>;

public class InitializePaymentCommandValidator : AbstractValidator<InitializePaymentCommand>
{
    public InitializePaymentCommandValidator()
    {
        RuleFor(x => x.Amount)
            .GreaterThan(0m)
            .LessThanOrEqualTo(10_000_000m)
            .Must(amount => amount == decimal.Round(amount, 2))
            .WithMessage("Amount must be greater than zero, no more than 10,000,000 NGN, and have at most two decimal places.");
    }
}

public class InitializePaymentCommandHandler
    : IRequestHandler<InitializePaymentCommand, Result<PaymentInitializationDto>>
{
    private readonly IApplicationDbContext _db;
    private readonly ICurrentUser _currentUser;
    private readonly IPaymentGateway _paymentGateway;

    public InitializePaymentCommandHandler(
        IApplicationDbContext db,
        ICurrentUser currentUser,
        IPaymentGateway paymentGateway)
    {
        _db = db;
        _currentUser = currentUser;
        _paymentGateway = paymentGateway;
    }

    public async Task<Result<PaymentInitializationDto>> Handle(
        InitializePaymentCommand request,
        CancellationToken cancellationToken)
    {
        if (!_currentUser.IsAuthenticated || _currentUser.UserId is null)
            return Result<PaymentInitializationDto>.Failure("User is not authenticated.", ErrorType.Unauthorized);

        // Keep money validation here too, even if a caller bypasses the MediatR pipeline.
        if (request.Amount <= 0m || request.Amount > 10_000_000m ||
            request.Amount != decimal.Round(request.Amount, 2))
        {
            return Result<PaymentInitializationDto>.Failure(
                "Amount must be greater than zero, no more than 10,000,000 NGN, and have at most two decimal places.",
                ErrorType.Validation);
        }

        var userId = _currentUser.UserId.Value;
        var user = await _db.Users.AsNoTracking()
            .FirstOrDefaultAsync(u => u.Id == userId, cancellationToken);
        if (user is null)
            return Result<PaymentInitializationDto>.Failure("User account not found.", ErrorType.NotFound);
        if (string.IsNullOrWhiteSpace(user.Email) || user.Email.Trim().Length > 200)
            return Result<PaymentInitializationDto>.Failure("A valid account email is required for payment.", ErrorType.Validation);

        var wallet = await _db.Wallets.AsNoTracking()
            .FirstOrDefaultAsync(w => w.UserId == userId, cancellationToken);
        if (wallet is null)
            return Result<PaymentInitializationDto>.Failure("Wallet not found.", ErrorType.NotFound);
        if (!wallet.IsActive)
            return Result<PaymentInitializationDto>.Failure("Wallet is currently restricted.", ErrorType.Forbidden);
        if (!string.Equals(wallet.Currency, "NGN", StringComparison.OrdinalIgnoreCase))
            return Result<PaymentInitializationDto>.Failure("Only NGN wallets can be funded through this gateway.", ErrorType.Validation);

        var reference = $"PAY-{Guid.NewGuid():N}";
        var intent = PaymentIntent.Create(
            userId: userId,
            walletId: wallet.Id,
            amount: request.Amount,
            reference: reference,
            customerEmail: user.Email,
            provider: _paymentGateway.ProviderName);

        // Persist first: a callback must be able to find this reference even if the
        // gateway responds slowly or this request is interrupted.
        await _db.PaymentIntents.AddAsync(intent, cancellationToken);
        await _db.SaveChangesAsync(cancellationToken);

        var gatewayResult = await _paymentGateway.InitializePaymentAsync(
            new PaymentInitializeRequest(
                CustomerEmail: user.Email,
                AmountInKobo: decimal.ToInt64(request.Amount * 100m),
                Reference: reference,
                Metadata: new Dictionary<string, string>
                {
                    ["purpose"] = "wallet_funding",
                    ["payment_intent_id"] = intent.Id.ToString()
                }),
            cancellationToken);

        // An error or timeout can be ambiguous; keep the intent pending rather
        // than assuming the gateway did not create a checkout.
        if (!gatewayResult.IsSuccess ||
            !string.Equals(gatewayResult.Reference, reference, StringComparison.Ordinal) ||
            string.IsNullOrWhiteSpace(gatewayResult.AuthorizationUrl) || gatewayResult.AuthorizationUrl.Trim().Length > 500 ||
            string.IsNullOrWhiteSpace(gatewayResult.AccessCode) || gatewayResult.AccessCode.Trim().Length > 120)
        {
            return Result<PaymentInitializationDto>.Failure(
                "Payment initialization could not be completed. Please try again.",
                ErrorType.Validation);
        }

        intent.SetCheckoutDetails(gatewayResult.AuthorizationUrl, gatewayResult.AccessCode);
        await _db.SaveChangesAsync(cancellationToken);

        return Result<PaymentInitializationDto>.Success(new PaymentInitializationDto(
            PaymentIntentId: intent.Id,
            Reference: intent.Reference,
            AuthorizationUrl: intent.AuthorizationUrl!,
            AccessCode: intent.AccessCode!,
            Amount: intent.Amount,
            Currency: intent.Currency,
            Provider: intent.Provider));
    }
}
