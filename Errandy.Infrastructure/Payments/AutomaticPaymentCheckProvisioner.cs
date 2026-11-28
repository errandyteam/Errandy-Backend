using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Errandy.Application.Features.Payments.Reconciliation;
using Errandy.Application.Interfaces;
using Errandy.Domain.Entities;
using Errandy.Domain.Enums;
using Errandy.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace Errandy.Infrastructure.Payments;

public enum AutomaticPaymentCheckProvisionResult
{
    Created,
    AlreadyScheduled,
    NotEligible
}

/// <summary>
/// Explicitly provisions scheduling state for ONE payment. This is not a worker,
/// does not discover payments, and never calls the gateway or changes a wallet.
/// It is deliberately not registered or invoked automatically yet.
/// </summary>
public sealed class AutomaticPaymentCheckProvisioner
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly AutomaticPaymentRetryPolicy _policy;

    public AutomaticPaymentCheckProvisioner(IServiceScopeFactory scopeFactory, AutomaticPaymentRetryPolicy policy)
    {
        _scopeFactory = scopeFactory ?? throw new ArgumentNullException(nameof(scopeFactory));
        _policy = policy ?? throw new ArgumentNullException(nameof(policy));
    }

    public async Task<AutomaticPaymentCheckProvisionResult> EnsureScheduledAsync(
        Guid paymentIntentId, CancellationToken cancellationToken = default)
    {
        if (paymentIntentId == Guid.Empty)
            throw new ArgumentException("Payment intent ID is required.", nameof(paymentIntentId));

        // A provisioner's commit must not be rolled back with an ambient caller
        // transaction. The scope also makes EF's normal SaveChanges transaction
        // the sole commit boundary for the insert.
        using var suppression = new System.Transactions.TransactionScope(
            System.Transactions.TransactionScopeOption.Suppress,
            System.Transactions.TransactionScopeAsyncFlowOption.Enabled);
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IApplicationDbContext>();
        var payment = await db.PaymentIntents.AsNoTracking()
            .Where(p => p.Id == paymentIntentId)
            .Select(p => new { p.Id, p.Status, p.Provider, p.CreatedAt })
            .SingleOrDefaultAsync(cancellationToken);

        if (payment is null || payment.Status != PaymentStatus.Pending ||
            !string.Equals(payment.Provider, "Paystack", StringComparison.OrdinalIgnoreCase))
            return AutomaticPaymentCheckProvisionResult.NotEligible;

        if (await db.AutomaticPaymentChecks.AsNoTracking()
            .AnyAsync(c => c.PaymentIntentId == paymentIntentId, cancellationToken))
            return AutomaticPaymentCheckProvisionResult.AlreadyScheduled;

        var firstAttemptAt = _policy.NextAttemptAtUtc(payment.CreatedAt, 0)
            ?? throw new InvalidOperationException("Initial retry time was not calculated.");
        db.AutomaticPaymentChecks.Add(AutomaticPaymentCheck.Schedule(
            payment.Id, firstAttemptAt, _policy.MaximumAttempts));

        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (
            ex.InnerException is PostgresException postgres &&
            postgres.SqlState == PostgresErrorCodes.UniqueViolation &&
            postgres.ConstraintName == "IX_AutomaticPaymentChecks_PaymentIntentId")
        {
            // Another provisioner inserted this payment's check. The failed
            // context is disposed on return; never reuse its tracked entities.
            return AutomaticPaymentCheckProvisionResult.AlreadyScheduled;
        }

        suppression.Complete();
        return AutomaticPaymentCheckProvisionResult.Created;
    }
}
