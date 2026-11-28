using Errandy.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Errandy.Infrastructure.Persistence.Configurations;

public class PaymentIntentConfiguration : IEntityTypeConfiguration<PaymentIntent>
{
    public void Configure(EntityTypeBuilder<PaymentIntent> builder)
    {
        builder.ToTable("PaymentIntents");
        builder.HasKey(p => p.Id);

        builder.Property(p => p.Amount).HasPrecision(18, 2).IsRequired();
        builder.Property(p => p.Currency).HasMaxLength(10).IsRequired();
        builder.Property(p => p.Provider).HasMaxLength(50).IsRequired();
        builder.Property(p => p.Reference).HasMaxLength(100).IsRequired();
        builder.Property(p => p.CustomerEmail).HasMaxLength(200).IsRequired();
        builder.Property(p => p.AuthorizationUrl).HasMaxLength(500);
        builder.Property(p => p.AccessCode).HasMaxLength(120);
        builder.Property(p => p.Channel).HasMaxLength(60);
        builder.Property(p => p.GatewayResponse).HasMaxLength(250);

        builder.HasIndex(p => p.Reference).IsUnique();
        builder.HasIndex(p => new { p.UserId, p.CreatedAt });
    }
}
