using Errandy.Application.Common.Interfaces;
using Errandy.Domain.Entities;
using Microsoft.EntityFrameworkCore;
namespace Errandy.Infrastructure.Persistence.Repositories;

public class UserRepository : IUserRepository
{
    private readonly ErrandyDbContext _db;
    public UserRepository(ErrandyDbContext db) => _db = db;
    public Task<User?> GetByIdAsync(Guid id, CancellationToken ct = default) =>
        _db.Users.Include(u => u.RunnerProfile)
            .FirstOrDefaultAsync(u => u.Id == id, ct);
    public Task<User?> GetByEmailAsync(string email, CancellationToken ct = default) =>
        _db.Users.FirstOrDefaultAsync(u => u.Email == email, ct);
    public Task<bool> ExistsByEmailAsync(string email, CancellationToken ct = default) =>
        _db.Users.AnyAsync(u => u.Email == email, ct);
    public async Task AddAsync(User user, CancellationToken ct = default) =>
        await _db.Users.AddAsync(user, ct);
    public void Update(User user) => _db.Users.Update(user);
}
