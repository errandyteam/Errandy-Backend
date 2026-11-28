using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Errandy.Application.Common.Interfaces;

// Amounts crossing the gateway boundary are in kobo, not naira.
public record PaymentInitializeRequest(
    string CustomerEmail,
    long AmountInKobo,
    string Reference,
    IReadOnlyDictionary<string, string>? Metadata = null);

public record PaymentInitializeResult(
    bool IsSuccess,
    string? Reference,
    string? AuthorizationUrl,
    string? AccessCode,
    string? ErrorMessage = null);

public record PaymentVerifyResult(
    bool IsSuccess,
    string? GatewayStatus,
    string? Reference,
    long? AmountInKobo,
    string? Currency,
    string? Channel,
    string? GatewayResponse,
    DateTime? PaidAtUtc,
    string? ErrorMessage = null);

public interface IPaymentGateway
{
    string ProviderName { get; }

    Task<PaymentInitializeResult> InitializePaymentAsync(
        PaymentInitializeRequest request,
        CancellationToken cancellationToken = default);

    Task<PaymentVerifyResult> VerifyPaymentAsync(
        string reference,
        CancellationToken cancellationToken = default);

    // Verify the exact bytes received, before parsing or re-serializing JSON.
    bool VerifyWebhookSignature(byte[] rawPayload, string signatureHeader);
}