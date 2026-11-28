using Errandy.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Errandy.Infrastructure.Persistence.Configurations;

public class AutomaticPaymentCheckConfiguration : IEntityTypeConfiguration<AutomaticPaymentCheck>
{
    public void Configure(EntityTypeBuilder<AutomaticPaymentCheck> builder)
    {
        builder.ToTable("AutomaticPaymentChecks");
        builder.HasKey(c => c.Id);

        builder.Property(c => c.Status).HasConversion<string>().HasMaxLength(24).IsRequired();
        builder.Property(c => c.LastResult).HasConversion<string>().HasMaxLength(24).IsRequired();
        // The domain model increments Revision on every transition. EF's UPDATE
        // must compare its originally loaded value in the WHERE clause.
        builder.Property(c => c.Revision).IsConcurrencyToken().ValueGeneratedNever();

        builder.HasOne<PaymentIntent>()
            .WithMany()
            .HasForeignKey(c => c.PaymentIntentId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(c => c.PaymentIntentId).IsUnique();
        builder.HasIndex(c => new { c.Status, c.NextAttemptAt, c.Id });
        builder.HasIndex(c => new { c.Status, c.LeaseExpiresAt, c.Id });
    }
}
