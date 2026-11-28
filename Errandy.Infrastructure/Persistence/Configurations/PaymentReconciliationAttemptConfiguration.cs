using Errandy.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Errandy.Infrastructure.Persistence.Configurations;

public class PaymentReconciliationAttemptConfiguration : IEntityTypeConfiguration<PaymentReconciliationAttempt>
{
    public void Configure(EntityTypeBuilder<PaymentReconciliationAttempt> builder)
    {
        builder.ToTable("PaymentReconciliationAttempts");
        builder.HasKey(a => a.Id);

        builder.Property(a => a.Reference).HasMaxLength(100).IsRequired();
        builder.Property(a => a.Reason).HasMaxLength(500).IsRequired();
        builder.Property(a => a.Outcome).HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(a => a.ResultCode).HasMaxLength(100);

        builder.HasOne<PaymentIntent>()
            .WithMany()
            .HasForeignKey(a => a.PaymentIntentId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(a => a.AdminUserId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(a => new { a.PaymentIntentId, a.RequestedAt });
        builder.HasIndex(a => new { a.AdminUserId, a.RequestedAt });
        builder.HasIndex(a => new { a.Outcome, a.RequestedAt });
    }
}
