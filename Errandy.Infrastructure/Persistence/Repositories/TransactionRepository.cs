using Errandy.Application.Common.Interfaces;
using Microsoft.EntityFrameworkCore;
using TransactionEntity = Errandy.Domain.Entities.Transaction;

namespace Errandy.Infrastructure.Persistence.Repositories;

public class TransactionRepository : ITransactionRepository
{
    private readonly ErrandyDbContext _db;

    public TransactionRepository(ErrandyDbContext db)
    {
        _db = db;
    }

    public async Task<TransactionEntity?> GetByIdAsync(Guid id, CancellationToken ct = default)
        => await _db.Transactions.FirstOrDefaultAsync(t => t.Id == id, ct);

    public async Task<TransactionEntity?> GetByReferenceAsync(string reference, CancellationToken ct = default)
        => await _db.Transactions.FirstOrDefaultAsync(t => t.Reference == reference, ct);

    public async Task<IReadOnlyList<TransactionEntity>> GetByWalletIdAsync(
        Guid walletId,
        int page = 1,
        int pageSize = 20,
        CancellationToken ct = default)
    {
        return await _db.Transactions
            .AsNoTracking()
            .Where(t => t.WalletId == walletId)
            .OrderByDescending(t => t.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(ct);
    }

    public async Task AddAsync(TransactionEntity transaction, CancellationToken ct = default)
        => await _db.Transactions.AddAsync(transaction, ct);
}