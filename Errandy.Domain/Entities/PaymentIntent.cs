using System;
using Errandy.Domain.Common;
using Errandy.Domain.Enums;

namespace Errandy.Domain.Entities;

/// <summary>
/// Tracks a wallet funding attempt from creation through verification.
/// This record must be saved before the gateway is contacted.
/// </summary>
public class PaymentIntent : BaseEntity
{
    public Guid UserId { get; private set; }
    public Guid WalletId { get; private set; }
    public Guid? TransactionId { get; private set; }

    public decimal Amount { get; private set; }
    public string Currency { get; private set; } = "NGN";
    public string Provider { get; private set; } = string.Empty;
    public string Reference { get; private set; } = string.Empty;
    public string CustomerEmail { get; private set; } = string.Empty;

    // These are populated only after checkout initialization succeeds.
    public string? AuthorizationUrl { get; private set; }
    public string? AccessCode { get; private set; }

    public PaymentStatus Status { get; private set; } = PaymentStatus.Pending;
    public string? Channel { get; private set; }
    public string? GatewayResponse { get; private set; }
    public DateTime? PaidAt { get; private set; }
    public DateTime? VerifiedAt { get; private set; }

    private PaymentIntent() { }

    public static PaymentIntent Create(
        Guid userId,
        Guid walletId,
        decimal amount,
        string reference,
        string customerEmail,
        string provider)
    {
        if (userId == Guid.Empty)
            throw new ArgumentException("User ID is required.", nameof(userId));
        if (walletId == Guid.Empty)
            throw new ArgumentException("Wallet ID is required.", nameof(walletId));
        if (amount <= 0m || amount > 9999999999999999.99m || amount != decimal.Round(amount, 2))
            throw new ArgumentOutOfRangeException(nameof(amount), "Amount must be positive, fit decimal(18,2), and have at most two decimal places.");
        if (string.IsNullOrWhiteSpace(reference) || reference.Trim().Length > 100)
            throw new ArgumentException("Reference is required and cannot exceed 100 characters.", nameof(reference));
        if (string.IsNullOrWhiteSpace(customerEmail) || customerEmail.Trim().Length > 200)
            throw new ArgumentException("Customer email is required and cannot exceed 200 characters.", nameof(customerEmail));
        if (string.IsNullOrWhiteSpace(provider) || provider.Trim().Length > 50)
            throw new ArgumentException("Provider is required and cannot exceed 50 characters.", nameof(provider));

        return new PaymentIntent
        {
            UserId = userId,
            WalletId = walletId,
            Amount = amount,
            Reference = reference.Trim(),
            CustomerEmail = customerEmail.Trim(),
            Provider = provider.Trim(),
            Currency = "NGN",
            Status = PaymentStatus.Pending
        };
    }

    public void SetCheckoutDetails(string authorizationUrl, string accessCode)
    {
        if (string.IsNullOrWhiteSpace(authorizationUrl) || authorizationUrl.Trim().Length > 500)
            throw new ArgumentException("Authorization URL is required and cannot exceed 500 characters.", nameof(authorizationUrl));
        if (string.IsNullOrWhiteSpace(accessCode) || accessCode.Trim().Length > 120)
            throw new ArgumentException("Access code is required and cannot exceed 120 characters.", nameof(accessCode));

        AuthorizationUrl = authorizationUrl.Trim();
        AccessCode = accessCode.Trim();
    }

    public void MarkSucceeded(
        Guid transactionId,
        string? channel,
        string? gatewayResponse,
        DateTime? paidAtUtc,
        DateTime verifiedAtUtc)
    {
        if (transactionId == Guid.Empty)
            throw new ArgumentException("Transaction ID is required.", nameof(transactionId));
        if (Status == PaymentStatus.Succeeded)
        {
            if (TransactionId != transactionId)
                throw new InvalidOperationException("Payment is already linked to another transaction.");
            return;
        }

        TransactionId = transactionId;
        Channel = channel;
        GatewayResponse = gatewayResponse;
        PaidAt = paidAtUtc;
        VerifiedAt = verifiedAtUtc;
        Status = PaymentStatus.Succeeded;
        UpdatedAt = verifiedAtUtc;
    }

    public void MarkFailed(string? gatewayResponse, DateTime verifiedAtUtc, bool abandoned = false)
    {
        if (Status == PaymentStatus.Succeeded)
            throw new InvalidOperationException("A successful payment cannot be marked as failed.");

        GatewayResponse = gatewayResponse;
        VerifiedAt = verifiedAtUtc;
        Status = abandoned ? PaymentStatus.Abandoned : PaymentStatus.Failed;
        UpdatedAt = verifiedAtUtc;
    }
}
