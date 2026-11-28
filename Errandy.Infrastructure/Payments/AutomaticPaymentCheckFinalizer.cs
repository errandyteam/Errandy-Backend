using System;
using System.Threading;
using System.Threading.Tasks;
using Errandy.Application.Common.DTOs;
using Errandy.Application.Common.Interfaces;
using Errandy.Application.Features.Payments.Reconciliation;
using Errandy.Application.Interfaces;
using Errandy.Domain.Entities;
using Errandy.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using WalletTransactionStatus = Errandy.Domain.Enums.TransactionStatus;

namespace Errandy.Infrastructure.Payments;

public enum AutomaticPaymentFinalizationResult
{
    Confirmed,
    Rescheduled,
    NeedsReview,
    LeaseLost,
    Contended
}

/// <summary>
/// Finalizes one check in a fresh scope. This type does not invoke the verifier,
/// gateway or worker and must not be passed a verifier's tracked context.
/// </summary>
public sealed class AutomaticPaymentCheckFinalizer
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IDateTimeProvider _clock;
    private readonly AutomaticPaymentRetryPolicy _policy;

    public AutomaticPaymentCheckFinalizer(
        IServiceScopeFactory scopeFactory, IDateTimeProvider clock, AutomaticPaymentRetryPolicy policy)
    {
        _scopeFactory = scopeFactory ?? throw new ArgumentNullException(nameof(scopeFactory));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        _policy = policy ?? throw new ArgumentNullException(nameof(policy));
    }

    /// <param name="confirmedPayment">
    /// Pass the verifier's non-null value ONLY when its Result.IsSuccess is true.
    /// Pass null for any failure, interruption or uncertain outcome. A non-null
    /// value is checked against current payment and funding-ledger records.
    /// </param>
    public async Task<AutomaticPaymentFinalizationResult> FinalizeAsync(
        AutomaticPaymentLease lease, PaymentVerificationDto? confirmedPayment,
        CancellationToken cancellationToken = default)
    {
        if (lease is null) throw new ArgumentNullException(nameof(lease));
        if (lease.CheckId == Guid.Empty || lease.PaymentIntentId == Guid.Empty || lease.Token == Guid.Empty)
            throw new ArgumentException("Valid lease identifiers are required.", nameof(lease));

        using var suppression = new System.Transactions.TransactionScope(
            System.Transactions.TransactionScopeOption.Suppress,
            System.Transactions.TransactionScopeAsyncFlowOption.Enabled);
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IApplicationDbContext>();
        var check = await db.AutomaticPaymentChecks.SingleOrDefaultAsync(c => c.Id == lease.CheckId, cancellationToken);
        if (check is null || check.PaymentIntentId != lease.PaymentIntentId ||
            check.Status != AutomaticPaymentCheckStatus.Leased || check.LeaseToken != lease.Token ||
            check.AttemptsStarted != lease.AttemptNumber)
            return AutomaticPaymentFinalizationResult.LeaseLost;

        var now = UtcNow();
        if (!HasLiveLease(check, now))
            return AutomaticPaymentFinalizationResult.LeaseLost;

        // Verify the success value belongs to this check and is consistent with
        // the current persisted intent and funding transaction. A mismatch is
        // unconfirmed, not evidence that the payment failed or was not credited.
        var confirmed = confirmedPayment is not null &&
            await MatchesPersistedFundingAsync(db, check.PaymentIntentId, confirmedPayment, cancellationToken);

        // The database reads may have taken long enough for the lease to expire,
        // or the wall clock may have moved backwards while they were running.
        var beforeReads = now;
        now = UtcNow();
        if (now < beforeReads || !HasLiveLease(check, now))
            return AutomaticPaymentFinalizationResult.LeaseLost;

        AutomaticPaymentFinalizationResult result;
        if (confirmed)
        {
            check.MarkConfirmed(lease.Token, now);
            result = AutomaticPaymentFinalizationResult.Confirmed;
        }
        else
        {
            var policyForCheck = new AutomaticPaymentRetryPolicy(
                check.MaximumAttempts, _policy.MinimumPaymentAge, _policy.InitialRetryDelay, _policy.MaximumRetryDelay);
            var next = policyForCheck.NextAttemptAtUtc(now, check.AttemptsStarted);
            check.MarkUnconfirmed(lease.Token, now, next);
            result = next.HasValue ? AutomaticPaymentFinalizationResult.Rescheduled : AutomaticPaymentFinalizationResult.NeedsReview;
        }

        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            // Another update won. Do not reuse this failed scope or assert a result.
            return AutomaticPaymentFinalizationResult.Contended;
        }

        // Other failures, including an ambiguous commit, propagate. The lease
        // can later be inspected/recovered in a fresh scope.
        suppression.Complete();
        return result;
    }

    private DateTime UtcNow()
    {
        var now = _clock.UtcNow;
        if (now.Kind != DateTimeKind.Utc)
            throw new InvalidOperationException("The clock must return UTC.");
        return now;
    }

    private static bool HasLiveLease(AutomaticPaymentCheck check, DateTime now) =>
        check.LastStartedAt is not null && check.LeaseExpiresAt is not null &&
        now >= check.LastStartedAt.Value && now < check.LeaseExpiresAt.Value;

    private static async Task<bool> MatchesPersistedFundingAsync(
        IApplicationDbContext db, Guid paymentIntentId, PaymentVerificationDto value, CancellationToken cancellationToken)
    {
        if (value.PaymentIntentId != paymentIntentId || value.Status != PaymentStatus.Succeeded || value.TransactionId is null)
            return false;

        var intent = await db.PaymentIntents.AsNoTracking()
            .SingleOrDefaultAsync(p => p.Id == paymentIntentId, cancellationToken);
        if (intent is null || intent.Status != PaymentStatus.Succeeded || intent.TransactionId != value.TransactionId ||
            !string.Equals(intent.Reference, value.Reference, StringComparison.Ordinal) ||
            intent.Amount != value.Amount || !string.Equals(intent.Currency, value.Currency, StringComparison.OrdinalIgnoreCase))
            return false;

        var walletExists = await db.Wallets.AsNoTracking()
            .AnyAsync(w => w.Id == intent.WalletId && w.UserId == intent.UserId, cancellationToken);
        if (!walletExists) return false;
        return await db.Transactions.AsNoTracking().AnyAsync(t =>
            t.Id == intent.TransactionId && t.WalletId == intent.WalletId && t.Reference == intent.Reference &&
            t.Amount == intent.Amount && t.Type == TransactionType.Credit && t.Category == TransactionCategory.Funding &&
            t.Status == WalletTransactionStatus.Completed, cancellationToken);
    }
}
