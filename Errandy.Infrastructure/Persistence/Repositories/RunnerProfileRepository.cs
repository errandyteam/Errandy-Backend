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
        _db.RunnerProfiles
            .Include(p => p.User)
            .FirstOrDefaultAsync(p => p.Id == id, ct);

    public Task<RunnerProfile?> GetByUserIdAsync(Guid userId, CancellationToken ct = default) =>
        _db.RunnerProfiles
            .Include(p => p.User)
            .FirstOrDefaultAsync(p => p.UserId == userId, ct);

    public async Task<IReadOnlyList<RunnerProfile>> GetPendingAsync(CancellationToken ct)
        => await _db.RunnerProfiles
            .AsNoTracking()
            .Include(p => p.User)
            .Where(p =>
                p.KycStatus == KycStatus.Pending

                // Old document fields.
                && p.DocumentType != null
                && p.DocumentType.Trim() != ""
                && p.DocumentNumber != null
                && p.DocumentNumber.Trim() != ""
                && p.DocumentImageUrl != null
                && p.DocumentImageUrl.Trim() != ""

                // Newer Government ID fields.
                && p.GovernmentIdType != null
                && p.GovernmentIdType.Trim() != ""
                && p.GovernmentIdNumber != null
                && p.GovernmentIdNumber.Trim() != ""
                && p.GovernmentIdImageUrl != null
                && p.GovernmentIdImageUrl.Trim() != ""

                // Selfie is required before admin review.
                && p.SelfieImageUrl != null
                && p.SelfieImageUrl.Trim() != ""

            /*
             * If BVN/bank details are mandatory before admin review,
             * uncomment this block in BOTH this repository and SubmitKycCommandHandler.
             *
             * && p.Bvn != null
             * && p.Bvn.Trim() != ""
             * && p.BankName != null
             * && p.BankName.Trim() != ""
             * && p.BankAccountNumber != null
             * && p.BankAccountNumber.Trim() != ""
             * && p.BankAccountName != null
             * && p.BankAccountName.Trim() != ""
             */
            )
            .OrderBy(p => p.CreatedAt)
            .ToListAsync(ct);

    public async Task AddAsync(RunnerProfile profile, CancellationToken ct = default) =>
        await _db.RunnerProfiles.AddAsync(profile, ct);

    public void Update(RunnerProfile profile) =>
        _db.RunnerProfiles.Update(profile);
}