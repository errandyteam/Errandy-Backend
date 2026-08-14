using Errandy.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Errandy.Infrastructure.Persistence.Configurations;

public class ErrandConfiguration : IEntityTypeConfiguration<Errand>
{
    public void Configure(EntityTypeBuilder<Errand> builder)
    {
        builder.ToTable("Errands");
        builder.HasKey(e => e.Id);

        builder.Property(e => e.Description).IsRequired().HasMaxLength(1000);
        builder.Property(e => e.EstimatedCost).HasColumnType("decimal(18,2)");
        builder.Property(e => e.FinalCost).HasColumnType("decimal(18,2)");
        builder.Property(e => e.Category).HasConversion<string>().HasMaxLength(50);
        builder.Property(e => e.Status).HasConversion<string>().HasMaxLength(50);

        builder.HasIndex(e => e.CustomerId);
        builder.HasIndex(e => e.RunnerId);
        builder.HasIndex(e => e.Status);

        // Errand.Messages is backed by a private field "_messages" — EF Core's
        // default convention maps this automatically, but we set it explicitly
        // to be safe since Messages only exposes a read-only getter.
        builder.HasMany(e => e.Messages)
            .WithOne()
            .HasForeignKey(m => m.ErrandId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.Navigation(e => e.Messages).UsePropertyAccessMode(PropertyAccessMode.Field);

        builder.HasOne(e => e.Proof)
            .WithOne()
            .HasForeignKey<Proof>(p => p.ErrandId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(e => e.Dispute)
            .WithOne()
            .HasForeignKey<Dispute>(d => d.ErrandId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}