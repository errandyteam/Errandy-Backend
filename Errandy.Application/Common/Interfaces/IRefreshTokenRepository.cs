using Errandy.Domain.Entities;

namespace Errandy.Application.Common.Interfaces;

public interface IRefreshTokenRepository
{
    Task<RefreshToken?> GetByHashAsync(string tokenHash, CancellationToken ct = default);
    Task AddAsync(RefreshToken rt, CancellationToken ct = default);
    void Update(RefreshToken rt);
}