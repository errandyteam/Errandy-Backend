using Errandy.Application.Common.Interfaces;
using Errandy.Domain.Entities;
using Errandy.Domain.Enums;
using Microsoft.EntityFrameworkCore;
namespace Errandy.Infrastructure.Persistence;
/// <summary>
/// Applies pending migrations and seeds a default admin account on startup.
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
        var adminExists = await db.Users.AnyAsync(u => u.Email == normalizedEmail);
        if (adminExists)
            return;
        var admin = new User
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
}
