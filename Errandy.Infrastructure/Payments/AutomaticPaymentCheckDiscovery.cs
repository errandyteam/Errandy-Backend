using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Errandy.Domain.Entities;
using Errandy.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace Errandy.Infrastructure.Payments;

/// <summary>
/// Read-only bounded queries used to discover candidates. These queries do not
/// claim, schedule, recover, verify or update anything. In production, callers
/// must pass the complete sets from the SAME fresh database context. Results are
/// snapshots and may become stale immediately; writes must recheck their state.
/// This class does not itself guarantee fairness under continuous failures or
/// concurrent workers; the future scheduler must address that and monitor it.
/// </summary>
public static class AutomaticPaymentCheckDiscovery
{
    public const int MaximumBatchSize = 100;

    public static async Task<IReadOnlyList<Guid>> FindUnscheduledPaymentsAsync(
        IQueryable<PaymentIntent> payments,
        IQueryable<AutomaticPaymentCheck> checks,
        int batchSize,
        CancellationToken cancellationToken = default)
    {
        if (payments is null) throw new ArgumentNullException(nameof(payments));
        if (checks is null) throw new ArgumentNullException(nameof(checks));
        ValidateBatchSize(batchSize);

        // Any existing check, including NeedsReview or Confirmed, prevents
        // resetting the attempt count. The provisioner rechecks and relies on
        // the unique index if a concurrent scanner selects the same payment.
        return await payments
            .Where(p => p.Status == PaymentStatus.Pending &&
                p.Provider.ToLower() == "paystack" &&
                !checks.Any(c => c.PaymentIntentId == p.Id))
            .OrderBy(p => p.CreatedAt)
            .ThenBy(p => p.Id)
            .Take(batchSize)
            .Select(p => p.Id)
            .ToListAsync(cancellationToken);
    }

    public static async Task<IReadOnlyList<Guid>> FindDueChecksAsync(
        IQueryable<AutomaticPaymentCheck> checks,
        DateTime nowUtc,
        int batchSize,
        CancellationToken cancellationToken = default)
    {
        if (checks is null) throw new ArgumentNullException(nameof(checks));
        ValidateTimeAndBatch(nowUtc, batchSize);
        return await checks
            .Where(c => c.Status == AutomaticPaymentCheckStatus.Scheduled &&
                c.NextAttemptAt != null && c.NextAttemptAt <= nowUtc)
            .OrderBy(c => c.NextAttemptAt)
            .ThenBy(c => c.Id)
            .Take(batchSize)
            .Select(c => c.Id)
            .ToListAsync(cancellationToken);
    }

    public static async Task<IReadOnlyList<Guid>> FindExpiredLeasesAsync(
        IQueryable<AutomaticPaymentCheck> checks,
        DateTime nowUtc,
        int batchSize,
        CancellationToken cancellationToken = default)
    {
        if (checks is null) throw new ArgumentNullException(nameof(checks));
        ValidateTimeAndBatch(nowUtc, batchSize);
        return await checks
            .Where(c => c.Status == AutomaticPaymentCheckStatus.Leased &&
                c.LeaseExpiresAt != null && c.LeaseExpiresAt <= nowUtc)
            .OrderBy(c => c.LeaseExpiresAt)
            .ThenBy(c => c.Id)
            .Take(batchSize)
            .Select(c => c.Id)
            .ToListAsync(cancellationToken);
    }

    private static void ValidateTimeAndBatch(DateTime nowUtc, int batchSize)
    {
        ValidateBatchSize(batchSize);
        if (nowUtc.Kind != DateTimeKind.Utc)
            throw new ArgumentException("Time must be UTC.", nameof(nowUtc));
    }

    private static void ValidateBatchSize(int batchSize)
    {
        if (batchSize < 1 || batchSize > MaximumBatchSize)
            throw new ArgumentOutOfRangeException(nameof(batchSize), "Batch size must be between 1 and 100.");
    }
}
