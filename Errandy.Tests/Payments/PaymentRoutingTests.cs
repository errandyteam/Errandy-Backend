using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Reflection;
using System.Security.Claims;
using System.Text;
using System.Text.Encodings.Web;
using System.Threading.Tasks;
using Errandy.Api.Controllers;
using Errandy.Application.Common.DTOs;
using Errandy.Application.Common.Results;
using Errandy.Application.Features.Payments.Commands.InitializePayment;
using Errandy.Application.Features.Payments.Commands.ProcessPaystackWebhook;
using MediatR;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.AspNetCore.TestHost;
using Xunit;

namespace Errandy.Tests.Payments;

// Isolated test host: deliberately does NOT execute the application's Program.cs,
// connect to a database, contact Paystack, or validate a real JWT.
public class PaymentRoutingTests
{
    [Fact]
    public async Task Initialize_WithoutAuthentication_ReturnsUnauthorized()
    {
        using var host = CreateHost(enabled: true);
        using var client = host.Server.CreateClient();

        var response = await client.PostAsJsonAsync("/api/payments/initialize", new { amount = 50m });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Null(host.Sender.LastRequest);
    }

    [Fact]
    public async Task Verify_WithoutAuthentication_ReturnsUnauthorized()
    {
        using var host = CreateHost(enabled: true);
        using var client = host.Server.CreateClient();

        var response = await client.PostAsJsonAsync("/api/payments/verify", new { reference = "PAY-test" });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Null(host.Sender.LastRequest);
    }

    [Fact]
    public async Task Initialize_WithTestIdentity_ReachesCommand()
    {
        using var host = CreateHost(enabled: true);
        using var client = host.Server.CreateClient();
        client.DefaultRequestHeaders.Add("Authorization", "Bearer test-only-token");

        var response = await client.PostAsJsonAsync("/api/payments/initialize", new { amount = 50m });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var command = Assert.IsType<InitializePaymentCommand>(host.Sender.LastRequest);
        Assert.Equal(50m, command.Amount);
    }

    [Fact]
    public async Task Webhook_IsAnonymousAndRoutesToHandler()
    {
        // This checks endpoint routing and anonymous access; the mediator is fake.
        // Signature validation is covered separately by the parser/handler tests.
        using var host = CreateHost(enabled: true);
        using var client = host.Server.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/payments/webhook/paystack");
        request.Headers.Add("x-paystack-signature", "dummy-signature");
        request.Content = new StringContent("{}", Encoding.UTF8, "application/json");

        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.IsType<ProcessPaystackWebhookCommand>(host.Sender.LastRequest);
    }

    [Fact]
    public async Task Webhook_FailedHandler_IsNotAcknowledged()
    {
        using var host = CreateHost(enabled: true);
        host.Sender.WebhookResponse = Result<bool>.Failure("Simulated failure", ErrorType.Conflict);
        using var client = host.Server.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/payments/webhook/paystack");
        request.Headers.Add("x-paystack-signature", "dummy-signature");
        request.Content = new StringContent("{}", Encoding.UTF8, "application/json");

        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
    }

    [Fact]
    public async Task DisabledPayments_ReturnServiceUnavailable_ForAuthenticatedUser()
    {
        using var host = CreateHost(enabled: false);
        using var client = host.Server.CreateClient();
        client.DefaultRequestHeaders.Add("Authorization", "Bearer test-only-token");

        var response = await client.PostAsJsonAsync("/api/payments/initialize", new { amount = 50m });

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Null(host.Sender.LastRequest);
    }

    private static TestHostSetup CreateHost(bool enabled)
    {
        var sender = DispatchProxy.Create<ISender, PaymentRoutingSenderProxy>();
        var proxy = (PaymentRoutingSenderProxy)sender;
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(
            new Dictionary<string, string?>
            {
                ["Payments:EnableEndpoints"] = enabled ? "true" : "false"
            }).Build();

        var builder = new WebHostBuilder()
            .ConfigureServices(services =>
            {
                services.AddSingleton<IConfiguration>(configuration);
                services.AddSingleton<ISender>(sender);
                services.AddAuthentication("Test")
                    .AddScheme<AuthenticationSchemeOptions, TestOnlyAuthenticationHandler>("Test", _ => { });
                services.AddAuthorization();
                services.AddControllers().AddApplicationPart(typeof(PaymentsController).Assembly);
            })
            .Configure(app =>
            {
                app.UseRouting();
                app.UseAuthentication();
                app.UseAuthorization();
                app.UseEndpoints(endpoints => endpoints.MapControllers());
            });

        return new TestHostSetup(new TestServer(builder), proxy);
    }

    private sealed class TestHostSetup : IDisposable
    {
        public TestServer Server { get; }
        public PaymentRoutingSenderProxy Sender { get; }
        public TestHostSetup(TestServer server, PaymentRoutingSenderProxy sender)
        {
            Server = server;
            Sender = sender;
        }
        public void Dispose() => Server.Dispose();
    }
}

// Only a test identity. This handler never validates or creates real JWTs.
public class TestOnlyAuthenticationHandler : AuthenticationHandler<AuthenticationSchemeOptions>
{
    public TestOnlyAuthenticationHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder) : base(options, logger, encoder) { }

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!string.Equals(Request.Headers["Authorization"].ToString(), "Bearer test-only-token", StringComparison.Ordinal))
            return Task.FromResult(AuthenticateResult.NoResult());

        var identity = new ClaimsIdentity(
            new[] { new Claim(ClaimTypes.NameIdentifier, Guid.Empty.ToString()) }, Scheme.Name);
        var ticket = new AuthenticationTicket(new ClaimsPrincipal(identity), Scheme.Name);
        return Task.FromResult(AuthenticateResult.Success(ticket));
    }
}

// Public and non-sealed so DispatchProxy can generate an implementation.
public class PaymentRoutingSenderProxy : DispatchProxy
{
    public object? LastRequest { get; private set; }
    public Result<bool> WebhookResponse { get; set; } = Result<bool>.Success(true);

    protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
    {
        if (targetMethod?.Name != "Send" || args is not { Length: > 0 })
            throw new InvalidOperationException("Unexpected mediator call in routing test.");
        LastRequest = args[0];

        if (args[0] is ProcessPaystackWebhookCommand)
            return Task.FromResult(WebhookResponse);
        if (args[0] is InitializePaymentCommand command)
        {
            var dto = new PaymentInitializationDto(Guid.NewGuid(), "PAY-test",
                "https://checkout.paystack.com/test", "test-code", command.Amount, "NGN", "Paystack");
            return Task.FromResult(Result<PaymentInitializationDto>.Success(dto));
        }
        throw new InvalidOperationException("Unexpected payment command in routing test.");
    }
}
