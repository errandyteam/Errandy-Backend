using Errandy.Application.Common.Interfaces;
using Errandy.Infrastructure.Persistence;
using Errandy.Infrastructure.Persistence.Repositories;
using Errandy.Infrastructure.Security;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
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
        // Expose the DbContext as the unit of work abstraction.
        services.AddScoped<IUnitOfWork>(sp => sp.GetRequiredService<ErrandyDbContext>());
        // ----- Repositories -----
        services.AddScoped<IUserRepository, UserRepository>();
        services.AddScoped<IRunnerProfileRepository, RunnerProfileRepository>();
        // ----- Security services -----
        services.Configure<JwtSettings>(configuration.GetSection(JwtSettings.SectionName));
        services.AddSingleton<IPasswordHasher, BCryptPasswordHasher>();
        services.AddSingleton<IJwtTokenGenerator, JwtTokenGenerator>();
        services.AddHttpContextAccessor();
        services.AddScoped<ICurrentUser, CurrentUser>();
        return services;
    }
}
