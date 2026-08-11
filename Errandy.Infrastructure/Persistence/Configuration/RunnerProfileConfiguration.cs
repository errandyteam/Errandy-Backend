using Errandy.Domain.Entities;
using Errandy.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
namespace Errandy.Infrastructure.Persistence.Configurations;

public class RunnerProfileConfiguration : IEntityTypeConfiguration<RunnerProfile>
{
    public void Configure(EntityTypeBuilder<RunnerProfile> builder)
    {
        builder.ToTable("RunnerProfiles");
        builder.HasKey(p => p.Id);
        builder.Property(p => p.KycStatus).HasConversion<string>().HasMaxLength(20);
        builder.Property(p => p.DocumentType).HasMaxLength(50);
        builder.Property(p => p.DocumentNumber).HasMaxLength(50);
        builder.Property(p => p.DocumentImageUrl).HasMaxLength(2048);
        builder.Property(p => p.RejectionReason).HasMaxLength(500);
        builder.Property(p => p.Rating).HasPrecision(3, 2);
        builder.Property(p => p.CompletionRate).HasPrecision(5, 2);
        builder.Property(p => p.CancellationRate).HasPrecision(5, 2);
        builder.HasIndex(p => p.UserId).IsUnique();
        builder.HasIndex(p => p.KycStatus);
    }
}
