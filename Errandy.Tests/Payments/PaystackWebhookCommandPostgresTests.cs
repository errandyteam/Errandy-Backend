using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Errandy.Application.Common.DTOs;
using Errandy.Application.Common.Interfaces;
using Errandy.Application.Common.Results;
using Errandy.Application.Features.Payments.Commands.ProcessPaystackWebhook;
using Errandy.Domain.Entities;
using Errandy.Domain.Enums;
using Errandy.Infrastructure.Payments;
using Errandy.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Npgsql;
using Xunit;

namespace Errandy.Tests.Payments;

public class PaystackWebhookCommandPostgresTests : IAsyncLifetime
{
    private const string DummySecret = "sk_test_dummy_secret_for_local_tests";
    private readonly string _connection = GetSafeTestConnection();
    private readonly Guid _userId = Guid.NewGuid();
    private readonly Guid _walletId = Guid.NewGuid();
    private readonly string _reference = $"PAY-{Guid.NewGuid():N}";
    private Guid _intentId;

    public async Task InitializeAsync()
    {
        await using var db = CreateContext();
        var intent = PaymentIntent.Create(_userId, _walletId, 50m, _reference,
            $"{_userId:N}@tests.invalid", "Paystack");
        _intentId = intent.Id;
        db.PaymentIntents.Add(intent);
        await db.SaveChangesAsync();
    }

    public async Task DisposeAsync()
    {
        await using var db = CreateContext();
        var intent = await db.PaymentIntents.SingleOrDefaultAsync(p => p.Id == _intentId && p.Reference == _reference);
        if (intent is not null)
        {
            db.PaymentIntents.Remove(intent);
            await db.SaveChangesAsync();
        }
    }

    [Fact]
    public async Task InvalidSignature_DoesNotCallVerification()
    {
        var verifier = new StubVerifier(false, _intentId, _walletId);
        await using var db = CreateContext();
        var handler = new ProcessPaystackWebhookCommandHandler(CreateGateway(), verifier, db);

        var result = await handler.Handle(
            new ProcessPaystackWebhookCommand(Payload("charge.success", _reference), new string('0', 128)),
            CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(ErrorType.Unauthorized, result.ErrorType);
        Assert.Equal(0, verifier.Calls);
    }

    [Fact]
    public async Task ValidSignature_WithUnknownReference_DoesNotCallVerification()
    {
        var verifier = new StubVerifier(false, _intentId, _walletId);
        await using var db = CreateContext();
        var handler = new ProcessPaystackWebhookCommandHandler(CreateGateway(), verifier, db);
        var payload = Payload("charge.success", $"UNKNOWN-{Guid.NewGuid():N}");

        var result = await handler.Handle(new ProcessPaystackWebhookCommand(payload, Sign(payload)), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(ErrorType.NotFound, result.ErrorType);
        Assert.Equal(0, verifier.Calls);
    }

    [Fact]
    public async Task FailedVerification_IsNotAcknowledgedAsSuccess()
    {
        var verifier = new StubVerifier(false, _intentId, _walletId);
        await using var db = CreateContext();
        var handler = new ProcessPaystackWebhookCommandHandler(CreateGateway(), verifier, db);
        var payload = Payload("charge.success", _reference);

        var result = await handler.Handle(new ProcessPaystackWebhookCommand(payload, Sign(payload)), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(ErrorType.Conflict, result.ErrorType);
        Assert.Equal(1, verifier.Calls);
        Assert.Equal(_userId, verifier.LastUserId);
        Assert.Equal(_reference, verifier.LastReference);
    }

    [Fact]
    public async Task SuccessfulVerification_IsAcknowledged()
    {
        // The verifier is a stub: this test checks dispatch/result handling,
        // not the actual wallet credit, which has separate PostgreSQL tests.
        var verifier = new StubVerifier(true, _intentId, _walletId);
        await using var db = CreateContext();
        var handler = new ProcessPaystackWebhookCommandHandler(CreateGateway(), verifier, db);
        var payload = Payload("charge.success", _reference);

        var result = await handler.Handle(new ProcessPaystackWebhookCommand(payload, Sign(payload)), CancellationToken.None);

        Assert.True(result.IsSuccess, result.Error);
        Assert.True(result.Value);
        Assert.Equal(1, verifier.Calls);
        Assert.Equal(_userId, verifier.LastUserId);
    }

    [Fact]
    public async Task SignedUnrelatedEvent_IsAcknowledgedWithoutVerification()
    {
        var verifier = new StubVerifier(false, _intentId, _walletId);
        await using var db = CreateContext();
        var handler = new ProcessPaystackWebhookCommandHandler(CreateGateway(), verifier, db);
        var payload = Payload("some.other.event", _reference);

        var result = await handler.Handle(new ProcessPaystackWebhookCommand(payload, Sign(payload)), CancellationToken.None);

        Assert.True(result.IsSuccess, result.Error);
        Assert.Equal(0, verifier.Calls);
    }

    private static byte[] Payload(string eventName, string reference) =>
        JsonSerializer.SerializeToUtf8Bytes(new { @event = eventName, data = new { reference } });

    private static string Sign(byte[] payload)
    {
        using var hmac = new HMACSHA512(Encoding.UTF8.GetBytes(DummySecret));
        return Convert.ToHexString(hmac.ComputeHash(payload)).ToLowerInvariant();
    }

    private static PaystackPaymentGateway CreateGateway() => new(
        Options.Create(new PaystackSettings { SecretKey = DummySecret }),
        NullLogger<PaystackPaymentGateway>.Instance,
        new NoNetworkCustomService());

    private ErrandyDbContext CreateContext() => new(
        new DbContextOptionsBuilder<ErrandyDbContext>().UseNpgsql(_connection).Options);

    private static string GetSafeTestConnection()
    {
        var connection = Environment.GetEnvironmentVariable("ERRANDY_PAYMENT_TEST_CONNECTION");
        if (string.IsNullOrWhiteSpace(connection))
            throw new InvalidOperationException("Set ERRANDY_PAYMENT_TEST_CONNECTION for the dedicated local test database.");
        NpgsqlConnectionStringBuilder parsed;
        try { parsed = new NpgsqlConnectionStringBuilder(connection); }
        catch (ArgumentException) { throw new InvalidOperationException("The test connection string is invalid."); }
        if (!string.Equals(parsed.Host, "localhost", StringComparison.OrdinalIgnoreCase) ||
            parsed.Port != 5432 ||
            !string.Equals(parsed.Database, "errandy_payment_tests", StringComparison.Ordinal))
            throw new InvalidOperationException("Tests are restricted to localhost:5432/errandy_payment_tests.");
        return connection;
    }

    private sealed class NoNetworkCustomService : ICustomService
    {
        public Task<ApiResponseModel> MakeHttpCall(object? request, string baseURL, string requestURL,
            HttpMethod method, Dictionary<string, string>? headers = null, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("This test must not make HTTP calls.");
    }

    private sealed class StubVerifier : IPaymentVerificationService
    {
        private readonly bool _succeed;
        private readonly Guid _intentId;
        private readonly Guid _walletId;
        public int Calls { get; private set; }
        public Guid LastUserId { get; private set; }
        public string? LastReference { get; private set; }

        public StubVerifier(bool succeed, Guid intentId, Guid walletId)
        {
            _succeed = succeed;
            _intentId = intentId;
            _walletId = walletId;
        }

        public Task<Result<PaymentVerificationDto>> VerifyOwnedPaymentAsync(
            string reference, Guid userId, CancellationToken cancellationToken = default)
        {
            Calls++;
            LastUserId = userId;
            LastReference = reference;
            if (!_succeed)
                return Task.FromResult(Result<PaymentVerificationDto>.Failure("Simulated failure.", ErrorType.Conflict));
            return Task.FromResult(Result<PaymentVerificationDto>.Success(new PaymentVerificationDto(
                PaymentIntentId: _intentId,
                Reference: reference,
                Status: PaymentStatus.Succeeded,
                StatusName: "Succeeded",
                Amount: 50m,
                Currency: "NGN",
                Channel: null,
                GatewayResponse: null,
                TransactionId: Guid.NewGuid(),
                WalletBalance: 150m,
                AlreadyCredited: false)));
        }
    }
}
