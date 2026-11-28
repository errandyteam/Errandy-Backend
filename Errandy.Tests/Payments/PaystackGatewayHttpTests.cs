using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Errandy.Application.Common.Interfaces;
using Errandy.Infrastructure.Payments;
using Errandy.Infrastructure.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Errandy.Tests.Payments;

public class PaystackGatewayHttpTests
{
    private const string DummySecret = "sk_test_dummy_secret_for_local_tests";
    private const string Reference = "PAY-example-123";

    [Fact]
    public async Task Initialize_SendsCorrectRequest_ThroughCustomService()
    {
        var handler = new FakeHttpHandler(HttpStatusCode.OK,
            "{\"status\":true,\"data\":{\"reference\":\"PAY-example-123\",\"authorization_url\":\"https://checkout.paystack.com/test\",\"access_code\":\"access123\"}}");
        var factory = new FakeHttpClientFactory(handler);
        var gateway = Gateway(factory, DummySecret, "https://example.test/payment-return");
        var request = new PaymentInitializeRequest(
            CustomerEmail: "customer@example.test",
            AmountInKobo: 12345L,
            Reference: Reference,
            Metadata: new Dictionary<string, string> { ["purpose"] = "wallet_funding" });

        var result = await gateway.InitializePaymentAsync(request);

        Assert.True(result.IsSuccess, result.ErrorMessage);
        Assert.Equal(Reference, result.Reference);
        Assert.Equal("https://checkout.paystack.com/test", result.AuthorizationUrl);
        Assert.Equal("access123", result.AccessCode);
        Assert.Equal("ExternalApi", factory.LastClientName);
        Assert.Equal(HttpMethod.Post, handler.LastMethod);
        Assert.Equal("https://api.paystack.co/transaction/initialize", handler.LastUri);
        Assert.Equal($"Bearer {DummySecret}", handler.LastAuthorization);

        Assert.NotNull(handler.LastBody);
        using var json = JsonDocument.Parse(handler.LastBody!);
        Assert.Equal("customer@example.test", json.RootElement.GetProperty("email").GetString());
        Assert.Equal("12345", json.RootElement.GetProperty("amount").GetString());
        Assert.Equal("NGN", json.RootElement.GetProperty("currency").GetString());
        Assert.Equal(Reference, json.RootElement.GetProperty("reference").GetString());
        Assert.Equal("https://example.test/payment-return", json.RootElement.GetProperty("callback_url").GetString());
        Assert.Equal("wallet_funding", json.RootElement.GetProperty("metadata").GetProperty("purpose").GetString());
    }

    [Fact]
    public async Task Verify_ReturnsGatewayDetails_InKobo()
    {
        var handler = new FakeHttpHandler(HttpStatusCode.OK,
            "{\"status\":true,\"data\":{\"status\":\"success\",\"reference\":\"PAY-example-123\",\"amount\":12345,\"currency\":\"NGN\",\"channel\":\"card\",\"gateway_response\":\"Approved\",\"paid_at\":\"2026-01-02T03:04:05Z\"}}");
        var gateway = Gateway(new FakeHttpClientFactory(handler), DummySecret);

        var result = await gateway.VerifyPaymentAsync(Reference);

        Assert.True(result.IsSuccess, result.ErrorMessage);
        Assert.Equal("success", result.GatewayStatus);
        Assert.Equal(Reference, result.Reference);
        Assert.Equal((long?)12345L, result.AmountInKobo);
        Assert.Equal("NGN", result.Currency);
        Assert.Equal("card", result.Channel);
        Assert.Equal((DateTime?)new DateTime(2026, 1, 2, 3, 4, 5, DateTimeKind.Utc), result.PaidAtUtc);
        Assert.Equal(HttpMethod.Get, handler.LastMethod);
        Assert.Equal("https://api.paystack.co/transaction/verify/PAY-example-123", handler.LastUri);
        Assert.Equal($"Bearer {DummySecret}", handler.LastAuthorization);
    }

    [Fact]
    public async Task MissingSecret_DoesNotMakeHttpCall()
    {
        var handler = new FakeHttpHandler(HttpStatusCode.OK, "{}");
        var gateway = Gateway(new FakeHttpClientFactory(handler), string.Empty);

        var result = await gateway.VerifyPaymentAsync(Reference);

        Assert.False(result.IsSuccess);
        Assert.Equal(0, handler.Calls);
    }

    [Fact]
    public async Task NonSuccessHttpStatus_IsReportedAsFailure()
    {
        var handler = new FakeHttpHandler(HttpStatusCode.BadGateway, "{\"status\":false}");
        var gateway = Gateway(new FakeHttpClientFactory(handler), DummySecret);

        var result = await gateway.VerifyPaymentAsync(Reference);

        Assert.False(result.IsSuccess);
        Assert.Equal(1, handler.Calls);
    }

    [Fact]
    public async Task MissingCurrency_IsNotTreatedAsNgn()
    {
        var handler = new FakeHttpHandler(HttpStatusCode.OK,
            "{\"status\":true,\"data\":{\"status\":\"success\",\"reference\":\"PAY-example-123\",\"amount\":12345}}");
        var gateway = Gateway(new FakeHttpClientFactory(handler), DummySecret);

        var result = await gateway.VerifyPaymentAsync(Reference);

        Assert.False(result.IsSuccess);
    }

    [Fact]
    public async Task Initialization_WithDifferentGatewayReference_IsRejected()
    {
        var handler = new FakeHttpHandler(HttpStatusCode.OK,
            "{\"status\":true,\"data\":{\"reference\":\"PAY-other\",\"authorization_url\":\"https://checkout.paystack.com/test\",\"access_code\":\"access123\"}}");
        var gateway = Gateway(new FakeHttpClientFactory(handler), DummySecret);

        var result = await gateway.InitializePaymentAsync(new PaymentInitializeRequest(
            "customer@example.test", 12345L, Reference));

        Assert.False(result.IsSuccess);
    }

    [Fact]
    public async Task InvalidJson_IsReportedAsFailure()
    {
        var handler = new FakeHttpHandler(HttpStatusCode.OK, "not-json");
        var gateway = Gateway(new FakeHttpClientFactory(handler), DummySecret);

        var result = await gateway.VerifyPaymentAsync(Reference);

        Assert.False(result.IsSuccess);
    }

    [Fact]
    public async Task CallerCancellation_IsPropagated()
    {
        var handler = new FakeHttpHandler(HttpStatusCode.OK, "{}");
        var gateway = Gateway(new FakeHttpClientFactory(handler), DummySecret);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            async () => await gateway.VerifyPaymentAsync(Reference, cancellation.Token));
    }

    private static PaystackPaymentGateway Gateway(
        IHttpClientFactory factory, string secret, string? callbackUrl = null) =>
        new(
            Options.Create(new PaystackSettings { SecretKey = secret, CallbackUrl = callbackUrl }),
            NullLogger<PaystackPaymentGateway>.Instance,
            new CustomService(factory));

    private sealed class FakeHttpClientFactory : IHttpClientFactory
    {
        private readonly HttpMessageHandler _handler;
        public string? LastClientName { get; private set; }
        public FakeHttpClientFactory(HttpMessageHandler handler) => _handler = handler;
        public HttpClient CreateClient(string name)
        {
            LastClientName = name;
            return new HttpClient(_handler, disposeHandler: false);
        }
    }

    private sealed class FakeHttpHandler : HttpMessageHandler
    {
        private readonly HttpStatusCode _status;
        private readonly string _responseBody;
        public int Calls { get; private set; }
        public HttpMethod? LastMethod { get; private set; }
        public string? LastUri { get; private set; }
        public string? LastAuthorization { get; private set; }
        public string? LastBody { get; private set; }

        public FakeHttpHandler(HttpStatusCode status, string responseBody)
        {
            _status = status;
            _responseBody = responseBody;
        }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Calls++;
            LastMethod = request.Method;
            LastUri = request.RequestUri?.AbsoluteUri;
            LastAuthorization = request.Headers.Authorization?.ToString();
            LastBody = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
            return new HttpResponseMessage(_status)
            {
                Content = new StringContent(_responseBody, Encoding.UTF8, "application/json")
            };
        }
    }
}
