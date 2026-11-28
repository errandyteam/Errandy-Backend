using Errandy.Application.Common.Interfaces;
using Errandy.Application.Features.Payments.Services;
using Errandy.Application.Interfaces;
using Errandy.Infrastructure.Payments;
using Errandy.Infrastructure.Kyc.QoreId;
using Errandy.Infrastructure.Persistence;
using Errandy.Infrastructure.Persistence.Repositories;
using Errandy.Infrastructure.Security;
using Errandy.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Net.Http;

namespace Errandy.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        // ----- Database provider selection -----
        // Set "DatabaseProvider" to "SqlServer" or "Postgres" in configuration.
        var provider = configuration.GetValue<string>("DatabaseProvider") ?? "Postgres";
        var connectionString = configuration.GetConnectionString("DefaultConnection");

        services.AddDbContext<ErrandyDbContext>(options =>
        {
            if (provider.Equals("SqlServer", StringComparison.OrdinalIgnoreCase))
                options.UseSqlServer(connectionString);
            else
                options.UseNpgsql(connectionString);
        });

        //HTTP Client for external API calls (I used a custom named client here, but you can also use the default HttpClient)
        services.AddHttpClient("ExternalApi", client =>
        {
            client.Timeout = TimeSpan.FromSeconds(30);
        })
        .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler
        {
            AllowAutoRedirect = false
        });

        services.AddScoped<ICustomService, CustomService>();

        //Payment verification service for handling payment verification logic
        services.AddScoped<IPaymentVerificationService, PaymentVerificationService>();

        //Paystack client for payment gateway integration
        services.Configure<PaystackSettings>(
        configuration.GetSection(PaystackSettings.SectionName));

        services.AddScoped<IPaymentGateway, PaystackPaymentGateway>();


        // ----- Shared database context -----
        services.AddScoped<IApplicationDbContext>(
            sp => sp.GetRequiredService<ErrandyDbContext>());

        // ----- Unit of Work -----
        services.AddScoped<IUnitOfWork>(
            sp => sp.GetRequiredService<ErrandyDbContext>());

        // ----- Repositories -----
        services.AddScoped<IUserRepository, UserRepository>();
        services.AddScoped<IRunnerProfileRepository, RunnerProfileRepository>();
        services.AddScoped<IWalletRepository, WalletRepository>();
        services.AddScoped<ITransactionRepository, TransactionRepository>();


        // ----- Financial / Escrow Services (Engineer B) -----
        services.AddScoped<IEscrowService, EscrowService>();

        // ----- KYC verification -----
        services.Configure<QoreIdSettings>(
            configuration.GetSection(QoreIdSettings.SectionName));

        services.AddScoped<ManualKycVerificationService>();

        services.AddHttpClient<QoreIdKycVerificationService>(client =>
        {
            client.Timeout = TimeSpan.FromSeconds(20);
        });

        services.AddScoped<IKycVerificationService>(sp =>
        {
            var kycProvider = (configuration["Kyc:Provider"] ?? "Manual")
                .Trim()
                .ToLowerInvariant();

            return kycProvider switch
            {
                "qoreid" => sp.GetRequiredService<QoreIdKycVerificationService>(),
                "manual" => sp.GetRequiredService<ManualKycVerificationService>(),
                _ => throw new InvalidOperationException(
                    $"Unknown Kyc:Provider '{kycProvider}'. Use 'Manual' or 'QoreId'.")
            };
        });

        // ----- Security services -----
        services.Configure<JwtSettings>(
            configuration.GetSection(JwtSettings.SectionName));

        services.AddSingleton<IPasswordHasher, BCryptPasswordHasher>();
        services.AddSingleton<IJwtTokenGenerator, JwtTokenGenerator>();
        services.AddSingleton<IDateTimeProvider, DateTimeProvider>();

        services.AddHttpContextAccessor();
        services.AddScoped<ICurrentUser, CurrentUser>();



        return services;
    }
}
