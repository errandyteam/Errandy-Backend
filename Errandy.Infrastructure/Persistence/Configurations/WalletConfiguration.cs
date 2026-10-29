using Errandy.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TransactionEntity = Errandy.Domain.Entities.Transaction;

namespace Errandy.Infrastructure.Persistence.Configurations;

public class WalletConfiguration : IEntityTypeConfiguration<Wallet>
{
    public void Configure(EntityTypeBuilder<Wallet> builder)
    {
        builder.ToTable("Wallets");

        builder.HasKey(w => w.Id);

        builder.Property(w => w.Balance)
            .HasPrecision(18, 2)
            .IsRequired();

        builder.Property(w => w.LockedBalance)
            .HasPrecision(18, 2)
            .IsRequired();

        builder.Ignore(w => w.TotalBalance);

        builder.Property(w => w.Currency)
            .HasMaxLength(3)
            .HasDefaultValue("NGN")
            .IsRequired();

        builder.Property(w => w.IsActive)
            .HasDefaultValue(true)
            .IsRequired();

        // One-to-One relationship: Every User has at most one Wallet
        builder.HasIndex(w => w.UserId)
            .IsUnique();

        builder.HasOne(w => w.User)
            .WithOne()
            .HasForeignKey<Wallet>(w => w.UserId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasMany<TransactionEntity>(w => w.Transactions)
            .WithOne(t => t.Wallet)
            .HasForeignKey(t => t.WalletId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}