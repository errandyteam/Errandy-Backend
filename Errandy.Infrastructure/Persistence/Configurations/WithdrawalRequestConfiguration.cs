using Errandy.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Errandy.Infrastructure.Persistence.Configurations;

public class WithdrawalRequestConfiguration : IEntityTypeConfiguration<WithdrawalRequest>
{
    public void Configure(EntityTypeBuilder<WithdrawalRequest> builder)
    {
        builder.ToTable("WithdrawalRequests");

        builder.HasKey(w => w.Id);

        builder.Property(w => w.Amount)
            .HasColumnType("decimal(18,2)")
            .IsRequired();

        builder.Property(w => w.Currency)
            .IsRequired()
            .HasMaxLength(10)
            .HasDefaultValue("NGN");

        builder.Property(w => w.BankName)
            .IsRequired()
            .HasMaxLength(120);

        builder.Property(w => w.BankCode)
            .HasMaxLength(20);

        builder.Property(w => w.AccountNumber)
            .IsRequired()
            .HasMaxLength(20);

        builder.Property(w => w.AccountName)
            .IsRequired()
            .HasMaxLength(150);

        builder.Property(w => w.Reference)
            .IsRequired()
            .HasMaxLength(100);

        builder.Property(w => w.AdminNote)
            .HasMaxLength(500);

        builder.HasIndex(w => w.Reference)
            .IsUnique();

        builder.HasIndex(w => new { w.Status, w.EligibleAt });
        builder.HasIndex(w => new { w.UserId, w.RequestedAt });
    }
}
