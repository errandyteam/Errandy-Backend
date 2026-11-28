using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Errandy.Domain.Entities;
using Errandy.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace Errandy.Infrastructure.Payments;

public sealed record AutomaticPaymentDiscoveryCursor(DateTime OrderAtUtc, Guid Id);

public sealed record AutomaticPaymentDiscoveryItem(Guid Id, DateTime OrderAtUtc)
{
    public AutomaticPaymentDiscoveryCursor ToCursor() => new(OrderAtUtc, Id);
}

/// <summary>
/// PostgreSQL keyset pages for discovery, ordered by timestamp then UUID. No
/// data is changed. Each cursor belongs only to the query and cutoff that
/// produced it. The caller must advance past individual failed items, wrap to
/// the beginning after reaching the end, and provide monitoring; persistence,
/// restart fairness and scheduling are NOT implemented by this class.
/// Concurrent row changes can move items between pages, so revisit on wrap.
/// Pass complete sets from the same fresh database context in production.
/// </summary>
public static class AutomaticPaymentCheckDiscoveryPages
{
    public static async Task<IReadOnlyList<AutomaticPaymentDiscoveryItem>> FindUnscheduledPaymentsAsync(
        IQueryable<PaymentIntent> payments,
        IQueryable<AutomaticPaymentCheck> checks,
        int batchSize,
        AutomaticPaymentDiscoveryCursor? after = null,
        CancellationToken cancellationToken = default)
    {
        if (payments is null) throw new ArgumentNullException(nameof(payments));
        if (checks is null) throw new ArgumentNullException(nameof(checks));
        Validate(batchSize, after);
        var query = payments.Where(p => p.Status == PaymentStatus.Pending &&
            p.Provider.ToLower() == "paystack" && !checks.Any(c => c.PaymentIntentId == p.Id));
        if (after is not null)
        {
            var time = after.OrderAtUtc;
            var id = after.Id;
            query = query.Where(p => EF.Functions.GreaterThan(
                ValueTuple.Create(p.CreatedAt, p.Id), ValueTuple.Create(time, id)));
        }
        return await query.OrderBy(p => p.CreatedAt).ThenBy(p => p.Id).Take(batchSize)
            .Select(p => new AutomaticPaymentDiscoveryItem(p.Id, p.CreatedAt))
            .ToListAsync(cancellationToken);
    }

    public static async Task<IReadOnlyList<AutomaticPaymentDiscoveryItem>> FindDueChecksAsync(
        IQueryable<AutomaticPaymentCheck> checks,
        DateTime cutoffUtc,
        int batchSize,
        AutomaticPaymentDiscoveryCursor? after = null,
        CancellationToken cancellationToken = default)
    {
        if (checks is null) throw new ArgumentNullException(nameof(checks));
        ValidateTime(cutoffUtc, nameof(cutoffUtc));
        Validate(batchSize, after);
        var query = checks.Where(c => c.Status == AutomaticPaymentCheckStatus.Scheduled &&
            c.NextAttemptAt != null && c.NextAttemptAt <= cutoffUtc);
        if (after is not null)
        {
            var time = after.OrderAtUtc;
            var id = after.Id;
            query = query.Where(c => EF.Functions.GreaterThan(
                ValueTuple.Create(c.NextAttemptAt!.Value, c.Id), ValueTuple.Create(time, id)));
        }
        return await query.OrderBy(c => c.NextAttemptAt).ThenBy(c => c.Id).Take(batchSize)
            .Select(c => new AutomaticPaymentDiscoveryItem(c.Id, c.NextAttemptAt!.Value))
            .ToListAsync(cancellationToken);
    }

    public static async Task<IReadOnlyList<AutomaticPaymentDiscoveryItem>> FindExpiredLeasesAsync(
        IQueryable<AutomaticPaymentCheck> checks,
        DateTime cutoffUtc,
        int batchSize,
        AutomaticPaymentDiscoveryCursor? after = null,
        CancellationToken cancellationToken = default)
    {
        if (checks is null) throw new ArgumentNullException(nameof(checks));
        ValidateTime(cutoffUtc, nameof(cutoffUtc));
        Validate(batchSize, after);
        var query = checks.Where(c => c.Status == AutomaticPaymentCheckStatus.Leased &&
            c.LeaseExpiresAt != null && c.LeaseExpiresAt <= cutoffUtc);
        if (after is not null)
        {
            var time = after.OrderAtUtc;
            var id = after.Id;
            query = query.Where(c => EF.Functions.GreaterThan(
                ValueTuple.Create(c.LeaseExpiresAt!.Value, c.Id), ValueTuple.Create(time, id)));
        }
        return await query.OrderBy(c => c.LeaseExpiresAt).ThenBy(c => c.Id).Take(batchSize)
            .Select(c => new AutomaticPaymentDiscoveryItem(c.Id, c.LeaseExpiresAt!.Value))
            .ToListAsync(cancellationToken);
    }

    private static void Validate(int batchSize, AutomaticPaymentDiscoveryCursor? after)
    {
        if (batchSize < 1 || batchSize > AutomaticPaymentCheckDiscovery.MaximumBatchSize)
            throw new ArgumentOutOfRangeException(nameof(batchSize), "Batch size must be between 1 and 100.");
        if (after is null) return;
        ValidateTime(after.OrderAtUtc, nameof(after));
        if (after.Id == Guid.Empty)
            throw new ArgumentException("Cursor ID must be nonempty.", nameof(after));
    }

    private static void ValidateTime(DateTime value, string name)
    {
        if (value.Kind != DateTimeKind.Utc)
            throw new ArgumentException("Time must be UTC.", name);
    }
}
