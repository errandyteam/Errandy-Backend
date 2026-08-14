using Errandy.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Errandy.Infrastructure.Persistence.Configurations;

public class ProofConfiguration : IEntityTypeConfiguration<Proof>
{
    public void Configure(EntityTypeBuilder<Proof> builder)
    {
        builder.ToTable("Proofs");
        builder.HasKey(p => p.Id);
        builder.Property(p => p.ImageUrl).IsRequired().HasMaxLength(2000);
        builder.Property(p => p.ReceiptUrl).HasMaxLength(2000);
        builder.HasIndex(p => p.ErrandId).IsUnique();
    }
}