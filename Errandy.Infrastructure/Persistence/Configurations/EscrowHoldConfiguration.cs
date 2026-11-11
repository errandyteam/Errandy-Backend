using Errandy.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Errandy.Infrastructure.Persistence.Configurations;

public class EscrowHoldConfiguration : IEntityTypeConfiguration<EscrowHold>
{
    public void Configure(EntityTypeBuilder<EscrowHold> builder)
    {
        builder.ToTable("EscrowHolds");

        builder.HasKey(h => h.Id);

        builder.Property(h => h.InitialLockedAmount)
            .HasColumnType("decimal(18,2)")
            .IsRequired();

        builder.Property(h => h.CurrentLockedAmount)
            .HasColumnType("decimal(18,2)")
            .IsRequired();

        builder.Property(h => h.ReleasedToRunnerAmount)
            .HasColumnType("decimal(18,2)")
            .IsRequired();

        builder.Property(h => h.CommissionAmount)
            .HasColumnType("decimal(18,2)")
            .IsRequired();

        builder.Property(h => h.RefundedToCustomerAmount)
            .HasColumnType("decimal(18,2)")
            .IsRequired();

        builder.Property(h => h.Currency)
            .IsRequired()
            .HasMaxLength(10)
            .HasDefaultValue("NGN");

        builder.Property(h => h.Status)
            .IsRequired();

        // One escrow hold per errand
        builder.HasIndex(h => h.ErrandId)
            .IsUnique();

        builder.HasIndex(h => new { h.CustomerId, h.Status });
        builder.HasIndex(h => h.RunnerId);
    }
}