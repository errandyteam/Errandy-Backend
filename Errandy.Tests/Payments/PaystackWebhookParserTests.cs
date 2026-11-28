using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Errandy.Application.Common.DTOs;
using Errandy.Application.Common.Interfaces;
using Errandy.Application.Features.Payments.Webhooks;
using Errandy.Infrastructure.Payments;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Errandy.Tests.Payments;

public class PaystackWebhookParserTests
{
    // A dummy value for tests only; it is not a real Paystack key.
    private const string TestSecret = "sk_test_dummy_secret_for_local_tests";

    [Fact]
    public void ValidChargeSuccess_ReturnsReference()
    {
        var payload = Bytes("{\"event\":\"charge.success\",\"data\":{\"reference\":\"PAY-example-123\"}}");

        var result = Parse(payload, Sign(payload));

        Assert.Equal(PaystackWebhookParseStatus.ChargeSucceeded, result.Status);
        Assert.Equal("PAY-example-123", result.Reference);
    }

    [Fact]
    public void ModifiedPayload_IsRejected()
    {
        var original = Bytes("{\"event\":\"charge.success\",\"data\":{\"reference\":\"PAY-original\"}}");
        var modified = Bytes("{\"event\":\"charge.success\",\"data\":{\"reference\":\"PAY-modified\"}}");

        var result = Parse(modified, Sign(original));

        Assert.Equal(PaystackWebhookParseStatus.InvalidSignature, result.Status);
        Assert.Null(result.Reference);
    }

    [Fact]
    public void InvalidHexSignature_IsRejected()
    {
        var payload = Bytes("{\"event\":\"charge.success\",\"data\":{\"reference\":\"PAY-example\"}}");

        var result = Parse(payload, new string('z', 128));

        Assert.Equal(PaystackWebhookParseStatus.InvalidSignature, result.Status);
    }

    [Fact]
    public void MissingSecret_DoesNotAcceptWebhook()
    {
        var payload = Bytes("{\"event\":\"charge.success\",\"data\":{\"reference\":\"PAY-example\"}}");
        var gateway = CreateGateway(string.Empty);

        var result = PaystackWebhookParser.Parse(payload, Sign(payload), gateway);

        Assert.Equal(PaystackWebhookParseStatus.InvalidSignature, result.Status);
    }

    [Fact]
    public void SignedUnknownEvent_IsIgnored()
    {
        var payload = Bytes("{\"event\":\"some.other.event\",\"data\":{}}");

        var result = Parse(payload, Sign(payload));

        Assert.Equal(PaystackWebhookParseStatus.Ignored, result.Status);
        Assert.Null(result.Reference);
    }

    [Fact]
    public void SignedMalformedJson_IsRejected()
    {
        var payload = Bytes("{not-valid-json");

        var result = Parse(payload, Sign(payload));

        Assert.Equal(PaystackWebhookParseStatus.InvalidPayload, result.Status);
    }

    [Theory]
    [InlineData("{\"event\":\"charge.success\",\"data\":{}}")]
    [InlineData("{\"event\":\"charge.success\",\"data\":{\"reference\":123}}")]
    [InlineData("{\"event\":\"charge.success\",\"data\":{\"reference\":\" PAY-example \"}}")]
    public void SignedChargeWithoutValidReference_IsRejected(string json)
    {
        var payload = Bytes(json);

        var result = Parse(payload, Sign(payload));

        Assert.Equal(PaystackWebhookParseStatus.InvalidPayload, result.Status);
    }

    [Fact]
    public void OversizedPayload_IsRejected()
    {
        var payload = new byte[PaystackWebhookParser.MaxPayloadBytes + 1];

        var result = Parse(payload, Sign(payload));

        Assert.Equal(PaystackWebhookParseStatus.InvalidPayload, result.Status);
    }

    private static PaystackWebhookParseResult Parse(byte[] payload, string signature) =>
        PaystackWebhookParser.Parse(payload, signature, CreateGateway(TestSecret));

    private static byte[] Bytes(string value) => Encoding.UTF8.GetBytes(value);

    private static string Sign(byte[] payload)
    {
        using var hmac = new HMACSHA512(Encoding.UTF8.GetBytes(TestSecret));
        return Convert.ToHexString(hmac.ComputeHash(payload)).ToLowerInvariant();
    }

    private static PaystackPaymentGateway CreateGateway(string secret) =>
        new(
            Options.Create(new PaystackSettings { SecretKey = secret }),
            NullLogger<PaystackPaymentGateway>.Instance,
            new NoNetworkCustomService());

    private sealed class NoNetworkCustomService : ICustomService
    {
        public Task<ApiResponseModel> MakeHttpCall(
            object? request,
            string baseURL,
            string requestURL,
            HttpMethod method,
            Dictionary<string, string>? headers = null,
            CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("Webhook parsing must not make HTTP calls.");
    }
}
