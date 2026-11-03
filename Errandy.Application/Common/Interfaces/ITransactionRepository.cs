using TransactionEntity = Errandy.Domain.Entities.Transaction;

namespace Errandy.Application.Common.Interfaces;

public interface ITransactionRepository
{
    Task<TransactionEntity?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<TransactionEntity?> GetByReferenceAsync(string reference, CancellationToken ct = default);
    Task<IReadOnlyList<TransactionEntity>> GetByWalletIdAsync(
        Guid walletId,
        int page = 1,
        int pageSize = 20,
        CancellationToken ct = default);
    Task AddAsync(TransactionEntity transaction, CancellationToken ct = default);
}