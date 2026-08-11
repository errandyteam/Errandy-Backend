using Errandy.Application.Common.Interfaces;
using Errandy.Domain.Common;
using Errandy.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using System.Reflection;

public class ErrandyDbContext : DbContext, IUnitOfWork
{
    public ErrandyDbContext(DbContextOptions<ErrandyDbContext> options) : base(options) { }
    public DbSet<User> Users => Set<User>();
    public DbSet<RunnerProfile> RunnerProfiles => Set<RunnerProfile>();
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.ApplyConfigurationsFromAssembly(Assembly.GetExecutingAssembly());
    }
    public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        foreach (var entry in ChangeTracker.Entries<BaseEntity>())
        {
            if (entry.State == EntityState.Modified)
                entry.Entity.UpdatedAt = DateTime.UtcNow;
        }
        return base.SaveChangesAsync(cancellationToken);
    }
}
