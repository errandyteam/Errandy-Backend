using Errandy.Domain.Entities;

namespace Errandy.Application.Common.Interfaces;

public interface IWalletRepository
{
    Task<Wallet?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<Wallet?> GetByUserIdAsync(Guid userId, CancellationToken ct = default);
    Task<bool> ExistsForUserAsync(Guid userId, CancellationToken ct = default);
    Task AddAsync(Wallet wallet, CancellationToken ct = default);
    void Update(Wallet wallet);
}
