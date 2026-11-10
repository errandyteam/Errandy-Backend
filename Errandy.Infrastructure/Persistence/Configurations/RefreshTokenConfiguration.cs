using Errandy.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Errandy.Infrastructure.Persistence.Configurations;

public class RefreshTokenConfiguration : IEntityTypeConfiguration<RefreshToken>
{
    public void Configure(EntityTypeBuilder<RefreshToken> builder)
    {
        builder.ToTable("RefreshTokens");
        builder.HasKey(rt => rt.Id);

        builder.Property(rt => rt.TokenHash).IsRequired().HasMaxLength(64);
        builder.Property(rt => rt.ReasonRevoked).HasMaxLength(200);

        builder.HasIndex(rt => rt.TokenHash).IsUnique();
        builder.HasIndex(rt => rt.UserId);
        builder.HasIndex(rt => rt.ExpiresAtUtc);

        builder.HasOne(rt => rt.User)
            .WithMany(u => u.RefreshTokens)
            .HasForeignKey(rt => rt.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(rt => rt.ReplacedBy)
            .WithOne()
            .HasForeignKey<RefreshToken>(rt => rt.ReplacedById)
            .OnDelete(DeleteBehavior.Restrict);
    }
}