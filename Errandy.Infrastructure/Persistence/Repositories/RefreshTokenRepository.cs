using Errandy.Application.Common.Interfaces;
using Errandy.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Errandy.Infrastructure.Persistence.Repositories;

public class RefreshTokenRepository : IRefreshTokenRepository
{
    private readonly AppDbContext _db;

    public RefreshTokenRepository(AppDbContext db) => _db = db;

    public Task<RefreshToken?> GetByHashAsync(string tokenHash, CancellationToken ct = default)
        => _db.RefreshTokens
            .Include(rt => rt.User)
            .FirstOrDefaultAsync(rt => rt.TokenHash == tokenHash, ct);

    public async Task AddAsync(RefreshToken rt, CancellationToken ct = default)
        => await _db.RefreshTokens.AddAsync(rt, ct);

    public void Update(RefreshToken rt) => _db.RefreshTokens.Update(rt);
}