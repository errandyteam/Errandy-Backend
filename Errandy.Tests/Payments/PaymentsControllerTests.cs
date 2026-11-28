using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Errandy.Api.Contracts;
using Errandy.Api.Controllers;
using Errandy.Application.Common.Results;
using Errandy.Application.Features.Payments.Commands.ProcessPaystackWebhook;
using Errandy.Application.Features.Payments.Webhooks;
using MediatR;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Errandy.Tests.Payments;

public class PaymentsControllerTests
{
    [Fact]
    public async Task DefaultConfiguration_DisablesAllPaymentActions()
    {
        var setup = CreateController(enabled: null);
        using var services = setup.Services;

        var initialize = await setup.Controller.Initialize(new InitializeWalletPaymentRequest(50m), CancellationToken.None);
        var verify = await setup.Controller.Verify(new VerifyWalletPaymentRequest("PAY-test"), CancellationToken.None);
        var webhook = await setup.Controller.PaystackWebhook(CancellationToken.None);

        Assert.Equal(503, Assert.IsType<StatusCodeResult>(initialize).StatusCode);
        Assert.Equal(503, Assert.IsType<StatusCodeResult>(verify).StatusCode);
        Assert.Equal(503, Assert.IsType<StatusCodeResult>(webhook).StatusCode);
        Assert.Null(setup.Sender.LastWebhook);
    }

    [Fact]
    public async Task Webhook_WithoutSignature_IsUnauthorized()
    {
        var setup = CreateController(enabled: true);
        using var services = setup.Services;

        var result = await setup.Controller.PaystackWebhook(CancellationToken.None);

        Assert.IsType<UnauthorizedResult>(result);
        Assert.Null(setup.Sender.LastWebhook);
    }

    [Fact]
    public async Task Webhook_DeclaredOversize_IsRejected()
    {
        var setup = CreateController(enabled: true);
        using var services = setup.Services;
        setup.Http.Request.ContentLength = PaystackWebhookParser.MaxPayloadBytes + 1;

        var result = await setup.Controller.PaystackWebhook(CancellationToken.None);

        Assert.Equal(413, Assert.IsType<StatusCodeResult>(result).StatusCode);
        Assert.Null(setup.Sender.LastWebhook);
    }

    [Fact]
    public async Task Webhook_ActualOversizeWithoutContentLength_IsRejected()
    {
        var setup = CreateController(enabled: true);
        using var services = setup.Services;
        setup.Http.Request.Headers["x-paystack-signature"] = "test-signature";
        setup.Http.Request.Body = new MemoryStream(new byte[PaystackWebhookParser.MaxPayloadBytes + 1]);

        var result = await setup.Controller.PaystackWebhook(CancellationToken.None);

        Assert.Equal(413, Assert.IsType<StatusCodeResult>(result).StatusCode);
        Assert.Null(setup.Sender.LastWebhook);
    }

    [Fact]
    public async Task Webhook_ReportsSuccessOnlyWhenHandlerSucceeds_AndPassesOriginalBytes()
    {
        var setup = CreateController(enabled: true);
        using var services = setup.Services;
        var payload = new byte[] { 123, 34, 120, 34, 58, 49, 125 };
        setup.Http.Request.Body = new MemoryStream(payload);
        setup.Http.Request.Headers["x-paystack-signature"] = "test-signature";
        setup.Sender.Response = Result<bool>.Success(true);

        var result = await setup.Controller.PaystackWebhook(CancellationToken.None);

        Assert.IsType<OkResult>(result);
        Assert.NotNull(setup.Sender.LastWebhook);
        Assert.Equal(payload, setup.Sender.LastWebhook!.RawPayload);
        Assert.Equal("test-signature", setup.Sender.LastWebhook.SignatureHeader);
    }

    [Theory]
    [InlineData(ErrorType.Unauthorized, 401)]
    [InlineData(ErrorType.Validation, 400)]
    [InlineData(ErrorType.NotFound, 503)]
    [InlineData(ErrorType.Conflict, 503)]
    public async Task Webhook_HandlerFailure_IsNeverAcknowledged(ErrorType errorType, int expectedStatus)
    {
        var setup = CreateController(enabled: true);
        using var services = setup.Services;
        setup.Http.Request.Body = new MemoryStream(new byte[] { 123, 125 });
        setup.Http.Request.Headers["x-paystack-signature"] = "test-signature";
        setup.Sender.Response = Result<bool>.Failure("Simulated failure", errorType);

        var result = await setup.Controller.PaystackWebhook(CancellationToken.None);

        var status = result switch
        {
            StatusCodeResult statusCodeResult => statusCodeResult.StatusCode,
            ObjectResult objectResult => objectResult.StatusCode,
            _ => null
        };
        Assert.Equal((int?)expectedStatus, status);
        Assert.NotNull(setup.Sender.LastWebhook);
    }

    private static Setup CreateController(bool? enabled)
    {
        var settings = new Dictionary<string, string?>();
        if (enabled.HasValue)
            settings["Payments:EnableEndpoints"] = enabled.Value ? "true" : "false";
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();
        var sender = DispatchProxy.Create<ISender, WebhookSenderProxy>();
        var proxy = (WebhookSenderProxy)sender;
        var services = new ServiceCollection().AddSingleton<ISender>(sender).BuildServiceProvider();
        var http = new DefaultHttpContext { RequestServices = services };
        var controller = new PaymentsController(configuration)
        {
            ControllerContext = new ControllerContext { HttpContext = http }
        };
        return new Setup(controller, proxy, services, http);
    }

    private sealed record Setup(
        PaymentsController Controller,
        WebhookSenderProxy Sender,
        ServiceProvider Services,
        DefaultHttpContext Http);
}

// Public and non-sealed so DispatchProxy can generate an implementation.
public class WebhookSenderProxy : DispatchProxy
{
    public Result<bool> Response { get; set; } = Result<bool>.Success(true);
    public ProcessPaystackWebhookCommand? LastWebhook { get; private set; }

    protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
    {
        if (targetMethod?.Name == "Send" && args is { Length: > 0 } &&
            args[0] is ProcessPaystackWebhookCommand webhook)
        {
            LastWebhook = webhook;
            return Task.FromResult(Response);
        }
        throw new InvalidOperationException("Unexpected mediator call in controller test.");
    }
}
