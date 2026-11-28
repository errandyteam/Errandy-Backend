using System;
using System.Threading;
using System.Threading.Tasks;
using Errandy.Application.Common.DTOs;
using Errandy.Application.Common.Interfaces;
using Errandy.Application.Interfaces;
using Errandy.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Errandy.Infrastructure.Payments;

public sealed record AutomaticPaymentExecutionResult(
    AutomaticPaymentClaimStatus ClaimStatus,
    AutomaticPaymentFinalizationResult? Finalization,
    bool VerifierCalled);

/// <summary>
/// Executes one explicitly supplied check. It is not a scheduler, is not
/// registered automatically, and must not be enabled until the remaining
/// financial safety and operational controls have been completed.
/// </summary>
public sealed class AutomaticPaymentCheckExecutor
{
    private static readonly TimeSpan FinalizationTimeout = TimeSpan.FromSeconds(5);
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IDateTimeProvider _clock;
    private readonly AutomaticPaymentCheckClaimer _claimer;
    private readonly AutomaticPaymentCheckFinalizer _finalizer;

    public AutomaticPaymentCheckExecutor(
        IServiceScopeFactory scopeFactory, IDateTimeProvider clock,
        AutomaticPaymentCheckClaimer claimer, AutomaticPaymentCheckFinalizer finalizer)
    {
        _scopeFactory = scopeFactory ?? throw new ArgumentNullException(nameof(scopeFactory));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        _claimer = claimer ?? throw new ArgumentNullException(nameof(claimer));
        _finalizer = finalizer ?? throw new ArgumentNullException(nameof(finalizer));
    }

    public async Task<AutomaticPaymentExecutionResult> ExecuteAsync(Guid checkId, CancellationToken cancellationToken = default)
    {
        // Suppress any caller transaction for the verifier as well as the claim
        // and finalization, each of which has its own independent commit.
        using var suppression = new System.Transactions.TransactionScope(
            System.Transactions.TransactionScopeOption.Suppress,
            System.Transactions.TransactionScopeAsyncFlowOption.Enabled);
        var claim = await _claimer.TryClaimAsync(checkId, cancellationToken);
        if (claim.Status != AutomaticPaymentClaimStatus.Claimed || claim.Lease is null)
            return new AutomaticPaymentExecutionResult(claim.Status, null, false);

        var lease = claim.Lease;
        PaymentVerificationDto? confirmedPayment = null;
        var verifierCalled = false;
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            // Read the owner and reference from persisted data, not from a caller.
            // This scope is discarded before the verification scope is created.
            var preflight = await ReadPreflightAsync(lease, cancellationToken);
            if (preflight.LeaseLost)
                return new AutomaticPaymentExecutionResult(claim.Status, AutomaticPaymentFinalizationResult.LeaseLost, false);

            if (preflight.IsEligible)
            {
                cancellationToken.ThrowIfCancellationRequested();
                using var scope = _scopeFactory.CreateScope();
                var verifier = scope.ServiceProvider.GetRequiredService<IPaymentVerificationService>();
                verifierCalled = true;
                var result = await verifier.VerifyOwnedPaymentAsync(preflight.Reference!, preflight.UserId, cancellationToken);
                if (result.IsSuccess && result.Value is not null)
                    confirmedPayment = result.Value;
            }
            // Other payment statuses/providers are not verified here. They are
            // unconfirmed, eventually requiring manual review, not marked failed.
        }
        catch (Exception verificationException)
        {
            // A failure or cancellation can leave verification/commit uncertain.
            // Use a fresh scope and short independent token to record unconfirmed.
            try
            {
                await FinalizeWithinLimitAsync(lease, null);
            }
            catch (Exception finalizationException)
            {
                throw new AggregateException("Verification and finalization both failed; inspect the check and payment state.",
                    verificationException, finalizationException);
            }
            throw;
        }

        var finalization = await FinalizeWithinLimitAsync(lease, confirmedPayment);
        suppression.Complete();
        return new AutomaticPaymentExecutionResult(claim.Status, finalization, verifierCalled);
    }

    private async Task<Preflight> ReadPreflightAsync(AutomaticPaymentLease lease, CancellationToken cancellationToken)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IApplicationDbContext>();
        var check = await db.AutomaticPaymentChecks.AsNoTracking()
            .SingleOrDefaultAsync(c => c.Id == lease.CheckId, cancellationToken);
        var now = _clock.UtcNow;
        if (now.Kind != DateTimeKind.Utc)
            throw new InvalidOperationException("The clock must return UTC.");
        if (check is null || check.PaymentIntentId != lease.PaymentIntentId ||
            check.Status != AutomaticPaymentCheckStatus.Leased || check.LeaseToken != lease.Token ||
            check.AttemptsStarted != lease.AttemptNumber || check.LastStartedAt is null || check.LeaseExpiresAt is null ||
            now < check.LastStartedAt.Value || now >= check.LeaseExpiresAt.Value)
            return new Preflight(true, false, null, Guid.Empty);

        var payment = await db.PaymentIntents.AsNoTracking()
            .SingleOrDefaultAsync(p => p.Id == check.PaymentIntentId, cancellationToken);
        if (payment is null || payment.UserId == Guid.Empty || string.IsNullOrWhiteSpace(payment.Reference) ||
            !string.Equals(payment.Provider, "Paystack", StringComparison.OrdinalIgnoreCase) ||
            (payment.Status != PaymentStatus.Pending && payment.Status != PaymentStatus.Succeeded))
            return new Preflight(false, false, null, Guid.Empty);

        // This is only a preflight snapshot: the lease can expire immediately
        // afterward, and other flows can change the payment. The shared verifier
        // must independently enforce payment and ledger safety.
        return new Preflight(false, true, payment.Reference, payment.UserId);
    }

    private async Task<AutomaticPaymentFinalizationResult> FinalizeWithinLimitAsync(
        AutomaticPaymentLease lease, PaymentVerificationDto? confirmedPayment)
    {
        using var timeout = new CancellationTokenSource(FinalizationTimeout);
        return await _finalizer.FinalizeAsync(lease, confirmedPayment, timeout.Token);
    }

    private sealed record Preflight(bool LeaseLost, bool IsEligible, string? Reference, Guid UserId);
}
