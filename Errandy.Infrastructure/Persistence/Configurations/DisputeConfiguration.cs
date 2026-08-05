using Errandy.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Errandy.Infrastructure.Persistence.Configurations;

public class DisputeConfiguration : IEntityTypeConfiguration<Dispute>
{
    public void Configure(EntityTypeBuilder<Dispute> builder)
    {
        builder.ToTable("Disputes");
        builder.HasKey(d => d.Id);

        builder.Property(d => d.Reason).HasConversion<string>().HasMaxLength(50);
        builder.Property(d => d.Status).HasConversion<string>().HasMaxLength(50);
        builder.Property(d => d.Resolution).HasConversion<string>().HasMaxLength(50);
        builder.Property(d => d.Description).HasMaxLength(2000);
        builder.Property(d => d.ResolutionNotes).HasMaxLength(2000);
        builder.Property(d => d.ResolutionAmount).HasColumnType("decimal(18,2)");

        builder.HasIndex(d => d.ErrandId).IsUnique();

        // EvidenceUrls is a read-only string list — store as a single
        // semicolon-delimited column rather than a child table, simplest for MVP.
        builder.Property(d => d.EvidenceUrls)
            .HasConversion(
                v => string.Join(';', v),
                v => v.Split(';', StringSplitOptions.RemoveEmptyEntries).ToList())
            .Metadata.SetValueComparer(new ValueComparer<IReadOnlyCollection<string>>(
                (c1, c2) => c1!.SequenceEqual(c2!),
                c => c.Aggregate(0, (a, v) => HashCode.Combine(a, v.GetHashCode())),
                c => c.ToList()));
    }
}