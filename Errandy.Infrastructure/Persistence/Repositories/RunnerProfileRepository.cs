using Errandy.Application.Common.Interfaces;
using Errandy.Domain.Entities;
using Errandy.Domain.Enums;
using Microsoft.EntityFrameworkCore;
namespace Errandy.Infrastructure.Persistence.Repositories;

public class RunnerProfileRepository : IRunnerProfileRepository
{
    private readonly ErrandyDbContext _db;
    public RunnerProfileRepository(ErrandyDbContext db) => _db = db;
    public Task<RunnerProfile?> GetByIdAsync(Guid id, CancellationToken ct = default) =>
        _db.RunnerProfiles.FirstOrDefaultAsync(p => p.Id == id, ct);
    public Task<RunnerProfile?> GetByUserIdAsync(Guid userId, CancellationToken ct = default) =>
        _db.RunnerProfiles.FirstOrDefaultAsync(p => p.UserId == userId, ct);
    public async Task<IReadOnlyList<RunnerProfile>> GetPendingAsync(CancellationToken ct = default) =>
        await _db.RunnerProfiles
            .Where(p => p.KycStatus == KycStatus.Pending && p.DocumentNumber != null)
            .OrderBy(p => p.CreatedAt)
            .ToListAsync(ct);
    public async Task AddAsync(RunnerProfile profile, CancellationToken ct = default) =>
        await _db.RunnerProfiles.AddAsync(profile, ct);
    public void Update(RunnerProfile profile) => _db.RunnerProfiles.Update(profile);
}