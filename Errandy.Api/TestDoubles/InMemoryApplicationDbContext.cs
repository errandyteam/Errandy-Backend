using Errandy.Application.Interfaces;
using Errandy.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Errandy.Api.TestDoubles;

/// <summary>
/// TEMPORARY — delete once Engineer A's real AppDbContext implements
/// IApplicationDbContext. This is an in-memory EF Core context so you can
/// run the full API locally (Swagger, Postman) and see your Create/Accept/
/// Complete flow actually persist data, without a real SQL Server/Postgres
/// instance or waiting on anyone else.
/// </summary>
public class InMemoryApplicationDbContext : DbContext, IApplicationDbContext
{
    public InMemoryApplicationDbContext(DbContextOptions<InMemoryApplicationDbContext> options)
        : base(options)
    {
    }

    public DbSet<Errand> Errands => Set<Errand>();
    public DbSet<Proof> Proofs => Set<Proof>();
    public DbSet<Message> Messages => Set<Message>();
    public DbSet<Dispute> Disputes => Set<Dispute>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // EF's in-memory provider is forgiving about missing configuration,
        // but wiring the real Configurations classes here too means you're
        // testing against the same mapping rules you'll use in production.
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(InMemoryApplicationDbContext).Assembly);
    }
}