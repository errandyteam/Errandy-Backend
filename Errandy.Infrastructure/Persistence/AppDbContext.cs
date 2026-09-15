using Errandy.Application.Common.Interfaces;
using Errandy.Application.Interfaces;
using Errandy.Domain.Common;
using Errandy.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using System.Reflection;

namespace Errandy.Infrastructure.Persistence;

public class ErrandyDbContext : DbContext, IUnitOfWork, IApplicationDbContext
{
    public ErrandyDbContext(
        DbContextOptions<ErrandyDbContext> options)
        : base(options)
    {
    }

    // Authentication / User Management
    public DbSet<User> Users => Set<User>();
    public DbSet<RunnerProfile> RunnerProfiles => Set<RunnerProfile>();

    // Errand / Chat / Dispute
    public DbSet<Errand> Errands => Set<Errand>();
    public DbSet<Proof> Proofs => Set<Proof>();
    public DbSet<Message> Messages => Set<Message>();
    public DbSet<Dispute> Disputes => Set<Dispute>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.ApplyConfigurationsFromAssembly(
            Assembly.GetExecutingAssembly());
    }

    public override Task<int> SaveChangesAsync(
        CancellationToken cancellationToken = default)
    {
        foreach (var entry in ChangeTracker.Entries<BaseEntity>())
        {
            if (entry.State == EntityState.Modified)
            {
                entry.Entity.UpdatedAt = DateTime.UtcNow;
            }
        }

        return base.SaveChangesAsync(cancellationToken);
    }
}