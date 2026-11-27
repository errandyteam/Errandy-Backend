using Errandy.Application.Common.Interfaces;
using Errandy.Application.Interfaces;
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
        // ---- KYC verification ----
        services.Configure<QoreIdSettings>(configuration.GetSection(QoreIdSettings.SectionName));

        services.AddSingleton<ManualKycVerificationService>();
        services.Configure<QoreIdSettings>(configuration.GetSection("QoreId"));

        services.AddHttpClient<QoreIdKycVerificationService>(client =>
        {
            client.Timeout = TimeSpan.FromSeconds(20);
        });

        services.AddScoped<ManualKycVerificationService>();

        services.AddScoped<IKycVerificationService>(sp =>
        {
            var provider = (configuration["Kyc:Provider"] ?? "Manual").Trim().ToLowerInvariant();

            return provider switch
            {
                "qoreid" => sp.GetRequiredService<QoreIdKycVerificationService>(),
                "manual" => sp.GetRequiredService<ManualKycVerificationService>(),
                _ => throw new InvalidOperationException(
                         $"Unknown Kyc:Provider '{provider}'. Use 'Manual' or 'QoreId'.")
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
