using Errandy.Application.Common.Interfaces;
using Errandy.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Errandy.Infrastructure.Persistence.Repositories;

public class WalletRepository : IWalletRepository
{
    private readonly ErrandyDbContext _db;

    public WalletRepository(ErrandyDbContext db)
    {
        _db = db;
    }

    public async Task<Wallet?> GetByIdAsync(Guid id, CancellationToken ct = default)
        => await _db.Wallets.FirstOrDefaultAsync(w => w.Id == id, ct);

    public async Task<Wallet?> GetByUserIdAsync(Guid userId, CancellationToken ct = default)
        => await _db.Wallets.FirstOrDefaultAsync(w => w.UserId == userId, ct);

    public async Task<bool> ExistsForUserAsync(Guid userId, CancellationToken ct = default)
        => await _db.Wallets.AnyAsync(w => w.UserId == userId, ct);

    public async Task AddAsync(Wallet wallet, CancellationToken ct = default)
        => await _db.Wallets.AddAsync(wallet, ct);

    public void Update(Wallet wallet)
        => _db.Wallets.Update(wallet);
}
