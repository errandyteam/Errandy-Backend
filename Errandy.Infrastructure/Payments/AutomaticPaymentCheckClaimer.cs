using System;
using System.Threading;
using System.Threading.Tasks;
using Errandy.Application.Common.Interfaces;
using Errandy.Application.Interfaces;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Errandy.Infrastructure.Payments;

public enum AutomaticPaymentClaimStatus
{
    Claimed,
    NotAvailable,
    Contended,
    LeaseUnusableAfterCommit
}

// Never log or expose the lease token to API callers.
public sealed record AutomaticPaymentLease(
    Guid CheckId,
    Guid PaymentIntentId,
    Guid Token,
    DateTime ExpiresAtUtc,
    int AttemptNumber,
    int MaximumAttempts);

public sealed record AutomaticPaymentClaimResult(
    AutomaticPaymentClaimStatus Status,
    AutomaticPaymentLease? Lease);

/// <summary>
/// Claims one known check by optimistic concurrency. This component neither
/// discovers work nor calls the verifier, and is not registered as a worker.
/// A returned lease is usable only after the insert/update commit succeeds.
/// </summary>
public sealed class AutomaticPaymentCheckClaimer
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IDateTimeProvider _clock;
    private readonly TimeSpan _leaseDuration;

    public AutomaticPaymentCheckClaimer(IServiceScopeFactory scopeFactory, IDateTimeProvider clock, TimeSpan leaseDuration)
    {
        _scopeFactory = scopeFactory ?? throw new ArgumentNullException(nameof(scopeFactory));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        if (leaseDuration <= TimeSpan.Zero || leaseDuration > TimeSpan.FromHours(24))
            throw new ArgumentOutOfRangeException(nameof(leaseDuration));
        _leaseDuration = leaseDuration;
    }

    public async Task<AutomaticPaymentClaimResult> TryClaimAsync(Guid checkId, CancellationToken cancellationToken = default)
    {
        if (checkId == Guid.Empty)
            throw new ArgumentException("Check ID is required.", nameof(checkId));

        // A claim must commit independently before a future worker can verify.
        using var suppression = new System.Transactions.TransactionScope(
            System.Transactions.TransactionScopeOption.Suppress,
            System.Transactions.TransactionScopeAsyncFlowOption.Enabled);
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IApplicationDbContext>();
        var check = await db.AutomaticPaymentChecks.SingleOrDefaultAsync(c => c.Id == checkId, cancellationToken);
        if (check is null)
            return new AutomaticPaymentClaimResult(AutomaticPaymentClaimStatus.NotAvailable, null);

        var now = _clock.UtcNow;
        var token = Guid.NewGuid();
        if (!check.TryStart(token, now, _leaseDuration))
            return new AutomaticPaymentClaimResult(AutomaticPaymentClaimStatus.NotAvailable, null);

        try
        {
            // The EF Revision concurrency token makes the database compare the
            // previously read revision. Losing contexts must be discarded.
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            return new AutomaticPaymentClaimResult(AutomaticPaymentClaimStatus.Contended, null);
        }

        // Any other error, including an ambiguous commit outcome, propagates.
        // No verification can be started from an exception; a committed lease,
        // if one exists, must later be recovered after expiry.
        var expires = check.LeaseExpiresAt!.Value;
        var returnTime = _clock.UtcNow;
        if (returnTime.Kind != DateTimeKind.Utc)
            throw new InvalidOperationException("The clock must return UTC.");
        suppression.Complete();
        if (returnTime < now || returnTime >= expires)
        {
            // The committed lease is left for explicit expiry recovery. Never
            // hand out a lease known to have expired (or after a backward clock jump).
            return new AutomaticPaymentClaimResult(AutomaticPaymentClaimStatus.LeaseUnusableAfterCommit, null);
        }
        var lease = new AutomaticPaymentLease(check.Id, check.PaymentIntentId, token,
            expires, check.AttemptsStarted, check.MaximumAttempts);
        return new AutomaticPaymentClaimResult(AutomaticPaymentClaimStatus.Claimed, lease);
    }
}
