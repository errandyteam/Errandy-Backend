using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Errandy.Infrastructure.Persistence;

public class ErrandyDbContextFactory : IDesignTimeDbContextFactory<ErrandyDbContext>
{
    public ErrandyDbContext CreateDbContext(string[] args)
    {
        var connectionString =
            Environment.GetEnvironmentVariable("ConnectionStrings__DefaultConnection");

        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException(
                "ConnectionStrings__DefaultConnection environment variable is required for EF design-time operations.");
        }

        var optionsBuilder = new DbContextOptionsBuilder<ErrandyDbContext>();

        optionsBuilder.UseNpgsql(connectionString);

        return new ErrandyDbContext(optionsBuilder.Options);
    }
}