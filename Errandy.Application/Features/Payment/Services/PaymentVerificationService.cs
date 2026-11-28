using System;
using System.Threading;
using System.Threading.Tasks;
using Errandy.Application.Common.DTOs;
using Errandy.Application.Common.Interfaces;
using Errandy.Application.Common.Results;
using Errandy.Application.Interfaces;
using Errandy.Domain.Entities;
using Errandy.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using TransactionEntity = Errandy.Domain.Entities.Transaction;
using WalletTransactionStatus = Errandy.Domain.Enums.TransactionStatus;

namespace Errandy.Application.Features.Payments.Services;

public class PaymentVerificationService : IPaymentVerificationService
{
    private readonly IApplicationDbContext _db;
    private readonly IPaymentGateway _paymentGateway;
    private readonly IDateTimeProvider _dateTimeProvider;

    public PaymentVerificationService(
        IApplicationDbContext db,
        IPaymentGateway paymentGateway,
        IDateTimeProvider dateTimeProvider)
    {
        _db = db;
        _paymentGateway = paymentGateway;
        _dateTimeProvider = dateTimeProvider;
    }

    public async Task<Result<PaymentVerificationDto>> VerifyOwnedPaymentAsync(
        string reference,
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        if (userId == Guid.Empty)
            return Result<PaymentVerificationDto>.Failure("User ID is required.", ErrorType.Unauthorized);
        if (string.IsNullOrWhiteSpace(reference) || reference.Trim().Length > 100)
            return Result<PaymentVerificationDto>.Failure("A valid payment reference is required.", ErrorType.Validation);

        reference = reference.Trim();
        var snapshot = await _db.PaymentIntents.AsNoTracking()
            .FirstOrDefaultAsync(p => p.Reference == reference && p.UserId == userId, cancellationToken);

        // Do not disclose whether another user's reference exists.
        if (snapshot is null)
            return Result<PaymentVerificationDto>.Failure("Payment not found.", ErrorType.NotFound);

        if (!string.Equals(snapshot.Provider, _paymentGateway.ProviderName, StringComparison.OrdinalIgnoreCase))
            return Result<PaymentVerificationDto>.Failure("Payment provider does not match the configured gateway.", ErrorType.Conflict);
        if (snapshot.Status == PaymentStatus.Succeeded)
            return await AlreadyCreditedAsync(snapshot, cancellationToken);

        // Network call happens before making any tracked wallet changes.
        var verification = await _paymentGateway.VerifyPaymentAsync(reference, cancellationToken);
        if (!verification.IsSuccess)
            return Result<PaymentVerificationDto>.Failure("Payment could not be verified. Please retry.", ErrorType.Validation);

        if (!string.Equals(verification.GatewayStatus, "success", StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(verification.Reference, reference, StringComparison.Ordinal) ||
            !string.Equals(verification.Currency, "NGN", StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(snapshot.Currency, "NGN", StringComparison.OrdinalIgnoreCase) ||
            verification.AmountInKobo != decimal.ToInt64(snapshot.Amount * 100m))
        {
            // Do not permanently fail an intent for pending or inconsistent gateway data.
            return Result<PaymentVerificationDto>.Failure(
                "Payment is not successful or its verified details do not match the payment intent.",
                ErrorType.Validation);
        }

        // Re-read after the network request; another callback may have completed it.
        var intent = await _db.PaymentIntents
            .FirstOrDefaultAsync(p => p.Reference == reference && p.UserId == userId, cancellationToken);
        if (intent is null)
            return Result<PaymentVerificationDto>.Failure("Payment not found.", ErrorType.NotFound);
        if (intent.Status == PaymentStatus.Succeeded)
            return await AlreadyCreditedAsync(intent, cancellationToken);

        // Reject changes to the immutable payment details between reads.
        if (intent.WalletId != snapshot.WalletId || intent.Amount != snapshot.Amount ||
            !string.Equals(intent.Provider, snapshot.Provider, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(intent.Currency, snapshot.Currency, StringComparison.OrdinalIgnoreCase))
            return Result<PaymentVerificationDto>.Failure("Payment details changed. Please retry.", ErrorType.Conflict);

        var wallet = await _db.Wallets
            .FirstOrDefaultAsync(w => w.Id == intent.WalletId && w.UserId == userId, cancellationToken);
        if (wallet is null)
            return Result<PaymentVerificationDto>.Failure("Associated wallet not found.", ErrorType.NotFound);
        if (!string.Equals(wallet.Currency, intent.Currency, StringComparison.OrdinalIgnoreCase))
            return Result<PaymentVerificationDto>.Failure("Wallet currency does not match the payment.", ErrorType.Conflict);

        var balanceBefore = wallet.Balance;
        // A wallet frozen after checkout must still receive verified funds, but
        // remains frozen; AdminCredit does not change its active status.
        if (wallet.IsActive)
            wallet.Credit(intent.Amount);
        else
            wallet.AdminCredit(intent.Amount);

        var transaction = new TransactionEntity
        {
            WalletId = wallet.Id,
            Type = TransactionType.Credit,
            Category = TransactionCategory.Funding,
            Amount = intent.Amount,
            BalanceBefore = balanceBefore,
            BalanceAfter = wallet.Balance,
            Status = WalletTransactionStatus.Completed,
            Reference = intent.Reference,
            Description = "Wallet funding via Paystack"
        };
        await _db.Transactions.AddAsync(transaction, cancellationToken);

        // Bound provider strings to the configured database column lengths.
        var channel = Limit(verification.Channel, 60);
        var gatewayResponse = Limit(verification.GatewayResponse, 250);
        intent.MarkSucceeded(transaction.Id, channel, gatewayResponse,
            verification.PaidAtUtc, _dateTimeProvider.UtcNow);

        try
        {
            // On PostgreSQL, EF Core's normal SaveChanges transaction commits or
            // rolls back the wallet, ledger row and intent update together.
            await _db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            // Do not retry the same tracked entities; a new request must reload them.
            return Result<PaymentVerificationDto>.Failure(
                "Wallet changed during verification. Please retry the payment verification.",
                ErrorType.Conflict);
        }

        return Result<PaymentVerificationDto>.Success(ToDto(intent, wallet.Balance, false));
    }

    private async Task<Result<PaymentVerificationDto>> AlreadyCreditedAsync(
        PaymentIntent intent,
        CancellationToken cancellationToken)
    {
        if (intent.TransactionId is null)
            return Result<PaymentVerificationDto>.Failure("Payment record requires reconciliation.", ErrorType.Conflict);

        var wallet = await _db.Wallets.AsNoTracking()
            .FirstOrDefaultAsync(w => w.Id == intent.WalletId && w.UserId == intent.UserId, cancellationToken);
        var ledgerEntryMatches = await _db.Transactions.AsNoTracking()
            .AnyAsync(t => t.Id == intent.TransactionId && t.WalletId == intent.WalletId &&
                t.Reference == intent.Reference && t.Amount == intent.Amount &&
                t.Type == TransactionType.Credit && t.Category == TransactionCategory.Funding &&
                t.Status == WalletTransactionStatus.Completed, cancellationToken);

        if (wallet is null || !ledgerEntryMatches)
            return Result<PaymentVerificationDto>.Failure("Payment record requires reconciliation.", ErrorType.Conflict);

        return Result<PaymentVerificationDto>.Success(ToDto(intent, wallet.Balance, true));
    }

    private static string? Limit(string? value, int maximumLength)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var trimmed = value.Trim();
        return trimmed.Length <= maximumLength ? trimmed : trimmed[..maximumLength];
    }

    private static PaymentVerificationDto ToDto(PaymentIntent intent, decimal balance, bool alreadyCredited) =>
        new(
            PaymentIntentId: intent.Id,
            Reference: intent.Reference,
            Status: intent.Status,
            StatusName: intent.Status.ToString(),
            Amount: intent.Amount,
            Currency: intent.Currency,
            Channel: intent.Channel,
            GatewayResponse: intent.GatewayResponse,
            TransactionId: intent.TransactionId,
            WalletBalance: balance,
            AlreadyCredited: alreadyCredited);
}
