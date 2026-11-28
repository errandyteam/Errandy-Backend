using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Errandy.Application.Common.DTOs;
using Errandy.Application.Common.Interfaces;
using Errandy.Application.Common.Results;
using Errandy.Application.Interfaces;
using Errandy.Domain.Entities;
using Errandy.Domain.Enums;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Errandy.Application.Features.Payments.Commands.AdminReconcilePayment;

public record AdminReconcilePaymentCommand(string Reference, string Reason)
    : IRequest<Result<AdminPaymentReconciliationDto>>;

public class AdminReconcilePaymentCommandValidator : AbstractValidator<AdminReconcilePaymentCommand>
{
    public AdminReconcilePaymentCommandValidator()
    {
        RuleFor(x => x.Reference).NotEmpty().MaximumLength(100);
        RuleFor(x => x.Reason).NotEmpty().MaximumLength(500);
    }
}

public class AdminReconcilePaymentCommandHandler
    : IRequestHandler<AdminReconcilePaymentCommand, Result<AdminPaymentReconciliationDto>>
{
    private readonly ICurrentUser _currentUser;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IDateTimeProvider _clock;

    public AdminReconcilePaymentCommandHandler(
        ICurrentUser currentUser,
        IServiceScopeFactory scopeFactory,
        IDateTimeProvider clock)
    {
        _currentUser = currentUser;
        _scopeFactory = scopeFactory;
        _clock = clock;
    }

    public async Task<Result<AdminPaymentReconciliationDto>> Handle(
        AdminReconcilePaymentCommand request,
        CancellationToken cancellationToken)
    {
        if (!_currentUser.IsAuthenticated || _currentUser.UserId is null)
            return Result<AdminPaymentReconciliationDto>.Failure("User is not authenticated.", ErrorType.Unauthorized);
        if (string.IsNullOrWhiteSpace(request.Reference) || request.Reference.Trim().Length > 100 ||
            string.IsNullOrWhiteSpace(request.Reason) || request.Reason.Trim().Length > 500)
            return Result<AdminPaymentReconciliationDto>.Failure("A valid reference and reason are required.", ErrorType.Validation);

        var adminId = _currentUser.UserId.Value;
        var reference = request.Reference.Trim();
        var reason = request.Reason.Trim();

        // Audit and settlement must not accidentally join a caller's ambient
        // transaction: each scoped SaveChanges must have its own commit boundary.
        using var suppression = new System.Transactions.TransactionScope(
            System.Transactions.TransactionScopeOption.Suppress,
            System.Transactions.TransactionScopeAsyncFlowOption.Enabled);

        Guid intentId;
        Guid ownerId;
        using (var scope = _scopeFactory.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<IApplicationDbContext>();
            var isAdmin = await db.Users.AsNoTracking()
                .AnyAsync(u => u.Id == adminId && u.Role == UserRole.Admin, cancellationToken);
            if (!isAdmin)
                return Result<AdminPaymentReconciliationDto>.Failure("Only administrators can reconcile payments.", ErrorType.Forbidden);

            var intent = await db.PaymentIntents.AsNoTracking()
                .Where(p => p.Reference == reference)
                .Select(p => new { p.Id, p.UserId })
                .FirstOrDefaultAsync(cancellationToken);
            if (intent is null)
                return Result<AdminPaymentReconciliationDto>.Failure("Payment not found.", ErrorType.NotFound);
            intentId = intent.Id;
            ownerId = intent.UserId;
        }

        // Commit the audit start before any gateway call or wallet credit.
        var auditAttemptId = await StartAuditAsync(intentId, adminId, reference, reason, cancellationToken);

        Result<PaymentVerificationDto> verification;
        try
        {
            // A failed SaveChanges can leave tracked entities dirty. The verifier
            // receives a different scope from both the audit-start and audit-finish scopes.
            using var scope = _scopeFactory.CreateScope();
            var verifier = scope.ServiceProvider.GetRequiredService<IPaymentVerificationService>();
            verification = await verifier.VerifyOwnedPaymentAsync(reference, ownerId, cancellationToken);
        }
        catch (Exception verificationException)
        {
            // A timeout or crash can leave the actual payment result uncertain.
            // Try to mark Unknown using an independent, bounded cancellation token.
            try
            {
                await FinishAuditAsync(auditAttemptId, PaymentReconciliationOutcome.Unknown, "VerificationInterrupted");
            }
            catch (Exception auditException)
            {
                // The committed Started row remains evidence even if finalization fails.
                throw new AggregateException("Verification and audit finalization both failed; inspect the started audit attempt.",
                    verificationException, auditException);
            }
            throw;
        }

        if (verification.IsSuccess && verification.Value is not null)
        {
            var code = verification.Value.AlreadyCredited ? "AlreadyCredited" : "Credited";
            await FinishAuditAsync(auditAttemptId, PaymentReconciliationOutcome.Succeeded, code);
            suppression.Complete();
            return Result<AdminPaymentReconciliationDto>.Success(
                new AdminPaymentReconciliationDto(auditAttemptId, verification.Value));
        }

        // A failed result does not establish the overall payment state: another
        // verification can have credited it concurrently. Do not infer a definite
        // non-completion from the verifier's generic Result.
        var resultCode = verification.IsSuccess ? "MissingVerificationResult" : "VerificationUnconfirmed";
        await FinishAuditAsync(auditAttemptId, PaymentReconciliationOutcome.Unknown, resultCode);
        suppression.Complete();

        var errorType = verification.IsSuccess || verification.ErrorType == ErrorType.None
            ? ErrorType.Conflict
            : verification.ErrorType;
        return Result<AdminPaymentReconciliationDto>.Failure(
            $"Payment outcome could not be confirmed by this attempt. Check the current payment state and audit history. Audit attempt: {auditAttemptId}.", errorType);
    }

    private async Task<Guid> StartAuditAsync(
        Guid intentId, Guid adminId, string reference, string reason, CancellationToken cancellationToken)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IApplicationDbContext>();
        var attempt = PaymentReconciliationAttempt.Start(intentId, adminId, reference, reason, _clock.UtcNow);
        await db.PaymentReconciliationAttempts.AddAsync(attempt, cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        return attempt.Id;
    }

    private async Task FinishAuditAsync(Guid attemptId, PaymentReconciliationOutcome outcome, string resultCode)
    {
        // Audit finalization should still be attempted if the HTTP caller disconnects.
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IApplicationDbContext>();
        var attempt = await db.PaymentReconciliationAttempts
            .SingleAsync(a => a.Id == attemptId, timeout.Token);
        attempt.Finish(outcome, resultCode, _clock.UtcNow);
        await db.SaveChangesAsync(timeout.Token);
    }
}
