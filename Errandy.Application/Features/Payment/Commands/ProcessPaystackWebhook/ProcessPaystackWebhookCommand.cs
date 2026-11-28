using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Errandy.Application.Common.Interfaces;
using Errandy.Application.Common.Results;
using Errandy.Application.Features.Payments.Webhooks;
using Errandy.Application.Interfaces;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace Errandy.Application.Features.Payments.Commands.ProcessPaystackWebhook;

public record ProcessPaystackWebhookCommand(
    byte[] RawPayload,
    string SignatureHeader) : IRequest<Result<bool>>;

public class ProcessPaystackWebhookCommandHandler
    : IRequestHandler<ProcessPaystackWebhookCommand, Result<bool>>
{
    private readonly IPaymentGateway _paymentGateway;
    private readonly IPaymentVerificationService _verificationService;
    private readonly IApplicationDbContext _db;

    public ProcessPaystackWebhookCommandHandler(
        IPaymentGateway paymentGateway,
        IPaymentVerificationService verificationService,
        IApplicationDbContext db)
    {
        _paymentGateway = paymentGateway;
        _verificationService = verificationService;
        _db = db;
    }

    public async Task<Result<bool>> Handle(
        ProcessPaystackWebhookCommand request,
        CancellationToken cancellationToken)
    {
        var parsed = PaystackWebhookParser.Parse(
            request.RawPayload, request.SignatureHeader, _paymentGateway);

        if (parsed.Status == PaystackWebhookParseStatus.InvalidSignature)
            return Result<bool>.Failure("Invalid webhook signature.", ErrorType.Unauthorized);
        if (parsed.Status == PaystackWebhookParseStatus.InvalidPayload)
            return Result<bool>.Failure("Invalid webhook payload.", ErrorType.Validation);
        if (parsed.Status == PaystackWebhookParseStatus.Ignored)
            return Result<bool>.Success(true);

        // A valid signature authenticates the event; never take an owner ID from it.
        // Resolve the owner from our own payment intent, then verify with Paystack.
        var reference = parsed.Reference!;
        var ownerId = await _db.PaymentIntents.AsNoTracking()
            .Where(p => p.Reference == reference)
            .Select(p => (Guid?)p.UserId)
            .FirstOrDefaultAsync(cancellationToken);

        if (ownerId is null)
            return Result<bool>.Failure("Payment intent not found; webhook requires investigation.", ErrorType.NotFound);

        var result = await _verificationService.VerifyOwnedPaymentAsync(
            reference, ownerId.Value, cancellationToken);

        if (!result.IsSuccess)
        {
            // Never acknowledge a charge as processed when crediting failed.
            // A non-success HTTP response lets Paystack retry the delivery.
            return Result<bool>.Failure("Payment processing did not complete; retry required.", ErrorType.Conflict);
        }

        return Result<bool>.Success(true);
    }
}
