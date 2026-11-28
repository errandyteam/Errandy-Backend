using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Reflection;
using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Threading.Tasks;
using Errandy.Api.Controllers;
using Errandy.Application.Common.DTOs;
using Errandy.Application.Common.Results;
using Errandy.Application.Features.Payments.Commands.AdminReconcilePayment;
using Errandy.Application.Features.Payments.Queries.GetAutomaticPaymentReviewQueue;
using Errandy.Application.Features.Payments.Queries.GetPaymentReconciliationHistory;
using Errandy.Application.Features.Payments.Queries.GetPendingPaymentReviews;
using MediatR;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Xunit;

namespace Errandy.Tests.Payments;

// Isolated HTTP routing tests with a fake identity and mediator. They do not run
// Program.cs, validate real JWTs, call handlers or connect to a database/Paystack.
public class AdminPaymentRoutingTests
{
    [Theory]
    [InlineData("pending")]
    [InlineData("retry")]
    [InlineData("history")]
    [InlineData("automatic-reviews")]
    public async Task WithoutAuthentication_EachRouteReturnsUnauthorized(string route)
    {
        using var host = CreateHost(true);
        using var client = host.Server.CreateClient();
        using var response = await SendAsync(client, route);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Null(host.Sender.LastRequest);
    }

    [Theory]
    [InlineData(null, "pending")]
    [InlineData(null, "retry")]
    [InlineData(null, "history")]
    [InlineData(null, "automatic-reviews")]
    [InlineData(false, "pending")]
    [InlineData(false, "retry")]
    [InlineData(false, "history")]
    [InlineData(false, "automatic-reviews")]
    public async Task DisabledOrUnsetSwitch_EachRouteReturnsServiceUnavailable(bool? enabled, string route)
    {
        using var host = CreateHost(enabled);
        using var client = host.Server.CreateClient();
        client.DefaultRequestHeaders.Add("Authorization", "Bearer test-only-token");
        using var response = await SendAsync(client, route);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Null(host.Sender.LastRequest);
    }

    [Fact]
    public async Task PendingRoute_BindsPaginationAndDispatchesQuery()
    {
        using var host = CreateHost(true);
        using var client = host.Server.CreateClient();
        client.DefaultRequestHeaders.Add("Authorization", "Bearer test-only-token");
        using var response = await client.GetAsync("/api/payments/admin/pending?page=2&pageSize=7");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var query = Assert.IsType<GetPendingPaymentReviewsQuery>(host.Sender.LastRequest);
        Assert.Equal(2, query.Page);
        Assert.Equal(7, query.PageSize);
    }

    [Fact]
    public async Task PendingRoute_DefaultsPagination()
    {
        using var host = CreateHost(true);
        using var client = host.Server.CreateClient();
        client.DefaultRequestHeaders.Add("Authorization", "Bearer test-only-token");
        using var response = await client.GetAsync("/api/payments/admin/pending");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var query = Assert.IsType<GetPendingPaymentReviewsQuery>(host.Sender.LastRequest);
        Assert.Equal(1, query.Page);
        Assert.Equal(50, query.PageSize);
    }

    [Fact]
    public async Task HistoryRoute_BindsReferenceAndPagination()
    {
        using var host = CreateHost(true);
        using var client = host.Server.CreateClient();
        client.DefaultRequestHeaders.Add("Authorization", "Bearer test-only-token");
        using var response = await client.GetAsync("/api/payments/admin/reconciliations/PAY-test?page=3&pageSize=5");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var query = Assert.IsType<GetPaymentReconciliationHistoryQuery>(host.Sender.LastRequest);
        Assert.Equal("PAY-test", query.Reference);
        Assert.Equal(3, query.Page);
        Assert.Equal(5, query.PageSize);
    }

    [Fact]
    public async Task HistoryRoute_DefaultsPagination()
    {
        using var host = CreateHost(true);
        using var client = host.Server.CreateClient();
        client.DefaultRequestHeaders.Add("Authorization", "Bearer test-only-token");
        using var response = await client.GetAsync("/api/payments/admin/reconciliations/PAY-test");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var query = Assert.IsType<GetPaymentReconciliationHistoryQuery>(host.Sender.LastRequest);
        Assert.Equal(1, query.Page);
        Assert.Equal(50, query.PageSize);
    }

    [Fact]
    public async Task AutomaticReviewRoute_BindsUserFilterAndPagination()
    {
        using var host = CreateHost(true);
        using var client = host.Server.CreateClient();
        client.DefaultRequestHeaders.Add("Authorization", "Bearer test-only-token");
        var userId = Guid.NewGuid();
        using var response = await client.GetAsync($"/api/payments/admin/automatic-reviews?page=2&pageSize=7&userId={userId}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var query = Assert.IsType<GetAutomaticPaymentReviewQueueQuery>(host.Sender.LastRequest);
        Assert.Equal(2, query.Page);
        Assert.Equal(7, query.PageSize);
        Assert.Equal((Guid?)userId, query.UserId);
    }

    [Fact]
    public async Task AutomaticReviewRoute_DefaultsPaginationAndHasNoUserFilter()
    {
        using var host = CreateHost(true);
        using var client = host.Server.CreateClient();
        client.DefaultRequestHeaders.Add("Authorization", "Bearer test-only-token");
        using var response = await client.GetAsync("/api/payments/admin/automatic-reviews");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var query = Assert.IsType<GetAutomaticPaymentReviewQueueQuery>(host.Sender.LastRequest);
        Assert.Equal(1, query.Page);
        Assert.Equal(50, query.PageSize);
        Assert.Null(query.UserId);
    }

    [Fact]
    public async Task AutomaticReviewRoute_MapsSimulatedForbiddenResult()
    {
        using var host = CreateHost(true);
        host.Sender.AutomaticReviewForbidden = true;
        using var client = host.Server.CreateClient();
        client.DefaultRequestHeaders.Add("Authorization", "Bearer test-only-token");
        using var response = await client.GetAsync("/api/payments/admin/automatic-reviews");
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.IsType<GetAutomaticPaymentReviewQueueQuery>(host.Sender.LastRequest);
    }

    [Fact]
    public async Task RetryRoute_BindsBodyAndReturnsHandlerForbiddenResult()
    {
        using var host = CreateHost(true);
        using var client = host.Server.CreateClient();
        client.DefaultRequestHeaders.Add("Authorization", "Bearer test-only-token");
        using var response = await client.PostAsJsonAsync("/api/payments/admin/reconcile",
            new { reference = "PAY-test", reason = "Review test" });
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        var command = Assert.IsType<AdminReconcilePaymentCommand>(host.Sender.LastRequest);
        Assert.Equal("PAY-test", command.Reference);
        Assert.Equal("Review test", command.Reason);
    }

    private static Task<HttpResponseMessage> SendAsync(HttpClient client, string route) => route switch
    {
        "pending" => client.GetAsync("/api/payments/admin/pending"),
        "history" => client.GetAsync("/api/payments/admin/reconciliations/PAY-test"),
        "automatic-reviews" => client.GetAsync("/api/payments/admin/automatic-reviews"),
        "retry" => client.PostAsJsonAsync("/api/payments/admin/reconcile", new { reference = "PAY-test", reason = "Review test" }),
        _ => throw new ArgumentOutOfRangeException(nameof(route))
    };

    private static HostSetup CreateHost(bool? enabled)
    {
        var sender = DispatchProxy.Create<ISender, AdminPaymentRoutingSenderProxy>();
        var proxy = (AdminPaymentRoutingSenderProxy)sender;
        var values = new Dictionary<string, string?>();
        if (enabled.HasValue) values["Payments:EnableEndpoints"] = enabled.Value ? "true" : "false";
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(values).Build();
        var builder = new WebHostBuilder()
            .ConfigureServices(services =>
            {
                services.AddSingleton<IConfiguration>(configuration);
                services.AddSingleton<ISender>(sender);
                services.AddAuthentication("Test")
                    .AddScheme<AuthenticationSchemeOptions, AdminRoutingTestAuthenticationHandler>("Test", _ => { });
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
        return new HostSetup(new TestServer(builder), proxy);
    }

    private sealed class HostSetup : IDisposable
    {
        public TestServer Server { get; }
        public AdminPaymentRoutingSenderProxy Sender { get; }
        public HostSetup(TestServer server, AdminPaymentRoutingSenderProxy sender) { Server = server; Sender = sender; }
        public void Dispose() => Server.Dispose();
    }
}

// This test scheme is not JWT authentication and does not establish an admin role.
public class AdminRoutingTestAuthenticationHandler : AuthenticationHandler<AuthenticationSchemeOptions>
{
    public AdminRoutingTestAuthenticationHandler(IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger, UrlEncoder encoder) : base(options, logger, encoder) { }

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!string.Equals(Request.Headers["Authorization"].ToString(), "Bearer test-only-token", StringComparison.Ordinal))
            return Task.FromResult(AuthenticateResult.NoResult());
        var identity = new ClaimsIdentity(new[] { new Claim(ClaimTypes.NameIdentifier, Guid.NewGuid().ToString()) }, Scheme.Name);
        return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identity), Scheme.Name)));
    }
}

// Public and non-sealed for DispatchProxy. No real admin authorization runs here.
public class AdminPaymentRoutingSenderProxy : DispatchProxy
{
    public object? LastRequest { get; private set; }
    public bool AutomaticReviewForbidden { get; set; }

    protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
    {
        if (targetMethod?.Name != "Send" || args is not { Length: > 0 })
            throw new InvalidOperationException("Unexpected mediator call in admin routing test.");
        LastRequest = args[0];
        if (args[0] is GetAutomaticPaymentReviewQueueQuery automatic)
        {
            if (AutomaticReviewForbidden)
                return Task.FromResult(Result<AutomaticPaymentReviewPageDto>.Failure("Simulated forbidden result", ErrorType.Forbidden));
            var page = new AutomaticPaymentReviewPageDto(Array.Empty<AutomaticPaymentReviewDto>(), automatic.Page, automatic.PageSize, false);
            return Task.FromResult(Result<AutomaticPaymentReviewPageDto>.Success(page));
        }
        if (args[0] is GetPendingPaymentReviewsQuery pending)
        {
            var page = new PendingPaymentReviewPageDto(Array.Empty<PendingPaymentReviewDto>(), pending.Page, pending.PageSize, false);
            return Task.FromResult(Result<PendingPaymentReviewPageDto>.Success(page));
        }
        if (args[0] is GetPaymentReconciliationHistoryQuery history)
        {
            var page = new PaymentReconciliationHistoryPageDto(Array.Empty<PaymentReconciliationHistoryItemDto>(), history.Page, history.PageSize, false);
            return Task.FromResult(Result<PaymentReconciliationHistoryPageDto>.Success(page));
        }
        if (args[0] is AdminReconcilePaymentCommand)
            return Task.FromResult(Result<AdminPaymentReconciliationDto>.Failure("Simulated forbidden result", ErrorType.Forbidden));
        throw new InvalidOperationException("Unexpected request in admin routing test.");
    }
}
