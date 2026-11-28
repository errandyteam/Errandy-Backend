using System;
using System.Text.Json;
using Errandy.Application.Common.Interfaces;

namespace Errandy.Application.Features.Payments.Webhooks;

public enum PaystackWebhookParseStatus
{
    InvalidSignature,
    InvalidPayload,
    Ignored,
    ChargeSucceeded
}

public record PaystackWebhookParseResult(
    PaystackWebhookParseStatus Status,
    string? Reference = null);

public static class PaystackWebhookParser
{
    // The API must also limit the request body BEFORE buffering it.
    public const int MaxPayloadBytes = 256 * 1024;

    public static PaystackWebhookParseResult Parse(
        byte[] rawPayload,
        string signatureHeader,
        IPaymentGateway gateway)
    {
        if (rawPayload is null || rawPayload.Length == 0 || rawPayload.Length > MaxPayloadBytes)
            return new(PaystackWebhookParseStatus.InvalidPayload);

        // Authenticate the exact bytes before inspecting any event fields.
        if (!gateway.VerifyWebhookSignature(rawPayload, signatureHeader))
            return new(PaystackWebhookParseStatus.InvalidSignature);

        try
        {
            using var document = JsonDocument.Parse(rawPayload);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object ||
                !root.TryGetProperty("event", out var eventElement) ||
                eventElement.ValueKind != JsonValueKind.String ||
                string.IsNullOrWhiteSpace(eventElement.GetString()))
                return new(PaystackWebhookParseStatus.InvalidPayload);

            if (!string.Equals(eventElement.GetString(), "charge.success", StringComparison.Ordinal))
                return new(PaystackWebhookParseStatus.Ignored);

            if (!root.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Object ||
                !data.TryGetProperty("reference", out var referenceElement) ||
                referenceElement.ValueKind != JsonValueKind.String)
                return new(PaystackWebhookParseStatus.InvalidPayload);

            var reference = referenceElement.GetString();
            if (string.IsNullOrWhiteSpace(reference) || reference.Length > 100 || reference != reference.Trim())
                return new(PaystackWebhookParseStatus.InvalidPayload);

            return new(PaystackWebhookParseStatus.ChargeSucceeded, reference);
        }
        catch (JsonException)
        {
            return new(PaystackWebhookParseStatus.InvalidPayload);
        }
    }
}
