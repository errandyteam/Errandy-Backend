using Errandy.Application.Common.Interfaces;
using Errandy.Domain.Entities;
using Errandy.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace Errandy.Infrastructure.Persistence;

/// <summary>
/// Applies pending migrations and seeds a default admin account (with wallet) on startup.
/// </summary>
public static class DatabaseSeeder
{
    public static async Task SeedAsync(
        ErrandyDbContext db,
        IPasswordHasher hasher,
        string adminEmail,
        string adminPassword)
    {
        await db.Database.MigrateAsync();
        var normalizedEmail = adminEmail.Trim().ToLowerInvariant();
        var admin = await db.Users.FirstOrDefaultAsync(u => u.Email == normalizedEmail);

        if (admin is null)
        {
            admin = new User
            {
                FullName = "Errandy Admin",
                Email = normalizedEmail,
                PhoneNumber = "+10000000000",
                PasswordHash = hasher.Hash(adminPassword),
                Role = UserRole.Admin
            };
            db.Users.Add(admin);
            await db.SaveChangesAsync();
        }

        var adminWalletExists = await db.Wallets.AnyAsync(w => w.UserId == admin.Id);
        if (!adminWalletExists)
        {
            db.Wallets.Add(new Wallet
            {
                UserId = admin.Id,
                Balance = 0m,
                LockedBalance = 0m,
                Currency = "NGN",
                IsActive = true
            });
            await db.SaveChangesAsync();
        }
    }
}
