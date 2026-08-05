using Errandy.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Errandy.Application.Interfaces;

/// <summary>
/// IMPORTANT — TEAM NOTE:
/// This only lists the DbSets Engineer C's features need (Errands, Proofs,
/// Messages, Disputes). Engineer A and Engineer B will each need their own
/// DbSets (Users, RunnerProfiles, Wallets, Transactions, Escrows).
///
/// In a single shared database, there should be ONE IApplicationDbContext
/// interface and ONE ApplicationDbContext class in Errandy.Infrastructure
/// that includes ALL DbSets from all three engineers — not three separate
/// interfaces. Raise this in your team sync so whoever writes the real
/// ApplicationDbContext extends this interface to include everyone's DbSets.
/// Until that's settled, Engineer C can code against this interface alone.
/// </summary>
public interface IApplicationDbContext
{
    DbSet<Errand> Errands { get; }
    DbSet<Proof> Proofs { get; }
    DbSet<Message> Messages { get; }
    DbSet<Dispute> Disputes { get; }

    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}
