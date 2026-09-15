using Errandy.Domain.Entities;
namespace Errandy.Application.Common.Interfaces;

public interface IRunnerProfileRepository
{
    Task<RunnerProfile?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<RunnerProfile?> GetByUserIdAsync(Guid userId, CancellationToken ct = default);
    Task<IReadOnlyList<RunnerProfile>> GetPendingAsync(CancellationToken ct = default);
    Task AddAsync(RunnerProfile profile, CancellationToken ct = default);
    void Update(RunnerProfile profile);
}
