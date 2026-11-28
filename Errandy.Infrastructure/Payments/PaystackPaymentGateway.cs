using System;
using System.Collections.Generic;
using System.Globalization;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Errandy.Application.Common.Interfaces;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Errandy.Infrastructure.Payments;

/// <summary>Paystack implementation; all gateway amounts are in kobo.</summary>
public class PaystackPaymentGateway : IPaymentGateway
{
    private const string ApiBaseUrl = "https://api.paystack.co";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly PaystackSettings _settings;
    private readonly ILogger<PaystackPaymentGateway> _logger;
    private readonly ICustomService _customService;

    public string ProviderName => "Paystack";

    public PaystackPaymentGateway(
        IOptions<PaystackSettings> settings,
        ILogger<PaystackPaymentGateway> logger,
        ICustomService customService)
    {
        _settings = settings.Value;
        _logger = logger;
        _customService = customService;
    }

    public async Task<PaymentInitializeResult> InitializePaymentAsync(
        PaymentInitializeRequest request,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(_settings.SecretKey))
            return InitializeFailure("Paystack is not configured.");
        if (request.AmountInKobo <= 0 || string.IsNullOrWhiteSpace(request.Reference) ||
            string.IsNullOrWhiteSpace(request.CustomerEmail))
            return InitializeFailure("Invalid payment initialization details.");
        if (!string.IsNullOrWhiteSpace(_settings.CallbackUrl) &&
            (!Uri.TryCreate(_settings.CallbackUrl, UriKind.Absolute, out var callback) ||
             callback.Scheme != Uri.UriSchemeHttps))
            return InitializeFailure("The configured Paystack callback URL must be an absolute HTTPS URL.");

        var payload = new InitializePayload
        {
            Email = request.CustomerEmail,
            Amount = request.AmountInKobo.ToString(CultureInfo.InvariantCulture),
            Currency = "NGN",
            Reference = request.Reference,
            CallbackUrl = string.IsNullOrWhiteSpace(_settings.CallbackUrl) ? null : _settings.CallbackUrl,
            Metadata = request.Metadata
        };

        try
        {
            var response = await _customService.MakeHttpCall(
                request: payload,
                baseURL: ApiBaseUrl,
                requestURL: "/transaction/initialize",
                method: HttpMethod.Post,
                headers: CreateHeaders(),
                cancellationToken: cancellationToken);

            if (response.StatusCode < 200 || response.StatusCode >= 300)
            {
                _logger.LogWarning("Paystack initialization returned HTTP {StatusCode}.", response.StatusCode);
                return InitializeFailure("Paystack could not initialize the payment.");
            }

            var envelope = JsonSerializer.Deserialize<PaystackEnvelope<InitializeData>>(response.Body, JsonOptions);
            var data = envelope?.Data;
            if (envelope?.Status != true || data is null ||
                !string.Equals(data.Reference, request.Reference, StringComparison.Ordinal) ||
                !Uri.TryCreate(data.AuthorizationUrl, UriKind.Absolute, out var checkoutUri) ||
                checkoutUri.Scheme != Uri.UriSchemeHttps || string.IsNullOrWhiteSpace(data.AccessCode))
                return InitializeFailure("Paystack returned invalid initialization details.");

            return new PaymentInitializeResult(true, data.Reference, data.AuthorizationUrl, data.AccessCode);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException or OperationCanceledException)
        {
            _logger.LogWarning(ex, "Paystack initialization failed.");
            return InitializeFailure("Paystack could not be reached or returned an invalid response.");
        }
    }

    public async Task<PaymentVerifyResult> VerifyPaymentAsync(
        string reference,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(_settings.SecretKey))
            return VerifyFailure("Paystack is not configured.");
        if (string.IsNullOrWhiteSpace(reference))
            return VerifyFailure("Payment reference is required.");

        try
        {
            var response = await _customService.MakeHttpCall(
                request: null,
                baseURL: ApiBaseUrl,
                requestURL: "/transaction/verify/" + Uri.EscapeDataString(reference),
                method: HttpMethod.Get,
                headers: CreateHeaders(),
                cancellationToken: cancellationToken);

            if (response.StatusCode < 200 || response.StatusCode >= 300)
            {
                _logger.LogWarning("Paystack verification returned HTTP {StatusCode}.", response.StatusCode);
                return VerifyFailure("Paystack could not verify the payment.");
            }

            var envelope = JsonSerializer.Deserialize<PaystackEnvelope<VerifyData>>(response.Body, JsonOptions);
            var data = envelope?.Data;
            if (envelope?.Status != true || data is null || string.IsNullOrWhiteSpace(data.Status) ||
                string.IsNullOrWhiteSpace(data.Reference) || data.Amount is null ||
                string.IsNullOrWhiteSpace(data.Currency))
                return VerifyFailure("Paystack returned incomplete verification details.");

            return new PaymentVerifyResult(
                IsSuccess: true,
                GatewayStatus: data.Status,
                Reference: data.Reference,
                AmountInKobo: data.Amount,
                Currency: data.Currency,
                Channel: data.Channel,
                GatewayResponse: data.GatewayResponse,
                PaidAtUtc: data.PaidAt?.UtcDateTime);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException or OperationCanceledException)
        {
            _logger.LogWarning(ex, "Paystack verification failed.");
            return VerifyFailure("Paystack could not be reached or returned an invalid response.");
        }
    }

    public bool VerifyWebhookSignature(byte[] rawPayload, string signatureHeader)
    {
        if (string.IsNullOrWhiteSpace(_settings.SecretKey) || rawPayload is null ||
            string.IsNullOrWhiteSpace(signatureHeader))
            return false;

        var signature = signatureHeader.Trim();
        if (signature.Length != 128)
            return false;

        try
        {
            var suppliedHash = Convert.FromHexString(signature);
            var key = Encoding.UTF8.GetBytes(_settings.SecretKey.Trim());
            var computedHash = HMACSHA512.HashData(key, rawPayload);
            return CryptographicOperations.FixedTimeEquals(computedHash, suppliedHash);
        }
        catch (FormatException)
        {
            return false;
        }
    }

    private Dictionary<string, string> CreateHeaders() => new()
    {
        ["Authorization"] = $"Bearer {_settings.SecretKey.Trim()}"
    };

    private static PaymentInitializeResult InitializeFailure(string message) =>
        new(false, null, null, null, message);

    private static PaymentVerifyResult VerifyFailure(string message) =>
        new(false, null, null, null, null, null, null, null, message);

    private sealed class PaystackEnvelope<T>
    {
        public PaystackEnvelope() { }
        [JsonPropertyName("status")]
        public bool Status { get; set; }
        [JsonPropertyName("data")]
        public T? Data { get; set; }
    }

    private sealed class InitializePayload
    {
        [JsonPropertyName("email")]
        public string Email { get; set; } = string.Empty;
        [JsonPropertyName("amount")]
        public string Amount { get; set; } = string.Empty;
        [JsonPropertyName("currency")]
        public string Currency { get; set; } = "NGN";
        [JsonPropertyName("reference")]
        public string Reference { get; set; } = string.Empty;
        [JsonPropertyName("callback_url")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? CallbackUrl { get; set; }
        [JsonPropertyName("metadata")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public IReadOnlyDictionary<string, string>? Metadata { get; set; }
    }

    private sealed class InitializeData
    {
        public InitializeData() { }
        [JsonPropertyName("reference")]
        public string? Reference { get; set; }
        [JsonPropertyName("authorization_url")]
        public string? AuthorizationUrl { get; set; }
        [JsonPropertyName("access_code")]
        public string? AccessCode { get; set; }
    }

    private sealed class VerifyData
    {
        public VerifyData() { }
        [JsonPropertyName("status")]
        public string? Status { get; set; }
        [JsonPropertyName("reference")]
        public string? Reference { get; set; }
        [JsonPropertyName("amount")]
        [JsonNumberHandling(JsonNumberHandling.AllowReadingFromString)]
        public long? Amount { get; set; }
        [JsonPropertyName("currency")]
        public string? Currency { get; set; }
        [JsonPropertyName("channel")]
        public string? Channel { get; set; }
        [JsonPropertyName("gateway_response")]
        public string? GatewayResponse { get; set; }
        [JsonPropertyName("paid_at")]
        public DateTimeOffset? PaidAt { get; set; }
    }
}
