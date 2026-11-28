using System;
using System.Threading;
using System.Threading.Tasks;
using Errandy.Application.Common.Interfaces;
using Errandy.Application.Features.Payments.Reconciliation;
using Errandy.Application.Interfaces;
using Errandy.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Errandy.Infrastructure.Payments;

public enum AutomaticPaymentRecoveryResult
{
    Rescheduled,
    NeedsReview,
    NotRecoverable,
    Contended
}

/// <summary>
/// Recovers one explicitly selected expired lease. Does not discover checks,
/// invoke the gateway or modify payments/wallets. Not registered as a worker.
/// </summary>
public sealed class AutomaticPaymentCheckRecovery
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IDateTimeProvider _clock;
    private readonly AutomaticPaymentRetryPolicy _policy;

    public AutomaticPaymentCheckRecovery(
        IServiceScopeFactory scopeFactory, IDateTimeProvider clock, AutomaticPaymentRetryPolicy policy)
    {
        _scopeFactory = scopeFactory ?? throw new ArgumentNullException(nameof(scopeFactory));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        _policy = policy ?? throw new ArgumentNullException(nameof(policy));
    }

    public async Task<AutomaticPaymentRecoveryResult> RecoverAsync(Guid checkId, CancellationToken cancellationToken = default)
    {
        if (checkId == Guid.Empty)
            throw new ArgumentException("Check ID is required.", nameof(checkId));

        using var suppression = new System.Transactions.TransactionScope(
            System.Transactions.TransactionScopeOption.Suppress,
            System.Transactions.TransactionScopeAsyncFlowOption.Enabled);
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IApplicationDbContext>();
        var check = await db.AutomaticPaymentChecks.SingleOrDefaultAsync(c => c.Id == checkId, cancellationToken);
        if (check is null)
            return AutomaticPaymentRecoveryResult.NotRecoverable;

        var now = _clock.UtcNow;
        if (now.Kind != DateTimeKind.Utc)
            throw new InvalidOperationException("The clock must return UTC.");
        if (check.Status != AutomaticPaymentCheckStatus.Leased || check.LeaseExpiresAt is null || now < check.LeaseExpiresAt.Value)
            return AutomaticPaymentRecoveryResult.NotRecoverable;

        // MaximumAttempts belongs to the persisted check. Changing the current
        // policy's default must not reset or prematurely exhaust existing work.
        var policyForCheck = new AutomaticPaymentRetryPolicy(
            check.MaximumAttempts, _policy.MinimumPaymentAge, _policy.InitialRetryDelay, _policy.MaximumRetryDelay);
        var next = policyForCheck.NextAttemptAtUtc(check.LeaseExpiresAt.Value, check.AttemptsStarted);
        check.RecoverExpiredLease(now, next);
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            // Another worker won; this scope and its tracked state are discarded.
            return AutomaticPaymentRecoveryResult.Contended;
        }

        // An ambiguous commit failure propagates. The caller must reload the
        // current row in a fresh scope instead of assuming recovery failed.
        suppression.Complete();
        return next.HasValue ? AutomaticPaymentRecoveryResult.Rescheduled : AutomaticPaymentRecoveryResult.NeedsReview;
    }
}
