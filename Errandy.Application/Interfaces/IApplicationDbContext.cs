using Errandy.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Errandy.Application.Interfaces;

public interface IApplicationDbContext
{
    // Authentication / User Management
    DbSet<User> Users { get; }
    DbSet<RunnerProfile> RunnerProfiles { get; }

    // Errand / Chat / Dispute
    DbSet<Errand> Errands { get; }
    DbSet<Proof> Proofs { get; }
    DbSet<Message> Messages { get; }
    DbSet<Dispute> Disputes { get; }

    Task<int> SaveChangesAsync(
        CancellationToken cancellationToken = default);
}