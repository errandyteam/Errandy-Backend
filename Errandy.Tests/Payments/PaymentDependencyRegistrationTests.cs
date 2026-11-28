using System;
using System.Collections.Generic;
using System.Data;
using System.Net.Http;
using Errandy.Application.Common.Interfaces;
using Errandy.Application.Features.Payments.Services;
using Errandy.Application.Interfaces;
using Errandy.Infrastructure;
using Errandy.Infrastructure.Payments;
using Errandy.Infrastructure.Persistence;
using Errandy.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Errandy.Tests.Payments;

// Exercises infrastructure registrations in isolation. No Program.cs, seeder,
// database connection, HTTP request, real authentication, or payout runs.
public class PaymentDependencyRegistrationTests
{
    [Fact]
    public void Infrastructure_ResolvesPaymentServicesWithoutOpeningDatabase()
    {
        using var provider = CreateProvider();
        using var scope = provider.CreateScope();
        var services = scope.ServiceProvider;

        Assert.IsType<CustomService>(services.GetRequiredService<ICustomService>());
        Assert.IsType<PaystackPaymentGateway>(services.GetRequiredService<IPaymentGateway>());
        Assert.IsType<PaymentVerificationService>(services.GetRequiredService<IPaymentVerificationService>());
        var db = services.GetRequiredService<ErrandyDbContext>();
        Assert.Same(db, services.GetRequiredService<IApplicationDbContext>());
        Assert.Equal(ConnectionState.Closed, db.Database.GetDbConnection().State);
    }

    [Fact]
    public void ExternalApiClient_HasThirtySecondTimeoutAndRedirectsDisabled()
    {
        using var provider = CreateProvider();
        var clients = provider.GetRequiredService<IHttpClientFactory>();
        using var client = clients.CreateClient("ExternalApi");
        Assert.Equal(TimeSpan.FromSeconds(30), client.Timeout);

        var handlers = provider.GetRequiredService<IHttpMessageHandlerFactory>();
        var handler = handlers.CreateHandler("ExternalApi");
        while (handler is DelegatingHandler delegating && delegating.InnerHandler is not null)
            handler = delegating.InnerHandler;
        var primaryHandler = Assert.IsType<HttpClientHandler>(handler);
        Assert.False(primaryHandler.AllowAutoRedirect);
        // The factory owns the handler; do not dispose it directly here.
    }

    private static ServiceProvider CreateProvider()
    {
        var settings = new Dictionary<string, string?>
        {
            ["DatabaseProvider"] = "Postgres",
            // Reserved invalid hostname ensures this is never a real database.
            ["ConnectionStrings:DefaultConnection"] = "Host=does-not-exist.invalid;Database=unused;Username=test-only"
        };
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IConfiguration>(configuration);
        services.AddInfrastructure(configuration);
        return services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
    }
}
