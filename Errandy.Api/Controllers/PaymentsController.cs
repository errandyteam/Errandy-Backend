using Errandy.Api.Contracts;
using Errandy.Application.Common.Results;
using Errandy.Application.Features.Payments.Commands.AdminReconcilePayment;
using Errandy.Application.Features.Payments.Commands.InitializePayment;
using Errandy.Application.Features.Payments.Commands.ProcessPaystackWebhook;
using Errandy.Application.Features.Payments.Commands.VerifyPayment;
using Errandy.Application.Features.Payments.Queries.GetAutomaticPaymentReviewQueue;
using Errandy.Application.Features.Payments.Queries.GetPaymentReconciliationHistory;
using Errandy.Application.Features.Payments.Queries.GetPendingPaymentReviews;
using Errandy.Application.Features.Payments.Webhooks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace Errandy.Api.Controllers;

[Authorize]
[Route("api/payments")]
public class PaymentsController : ApiControllerBase
{
    private readonly IConfiguration _configuration;

    public PaymentsController(IConfiguration configuration)
    {
        _configuration = configuration;
    }

    // Disabled unless explicitly enabled in configuration. Do not enable on a
    // shared or production database until financial safety tests are complete.
    private bool PaymentsEnabled =>
        bool.TryParse(_configuration["Payments:EnableEndpoints"], out var enabled) && enabled;

    [HttpPost("initialize")]
    public async Task<IActionResult> Initialize(
        [FromBody] InitializeWalletPaymentRequest request,
        CancellationToken cancellationToken)
    {
        if (!PaymentsEnabled)
            return StatusCode(StatusCodes.Status503ServiceUnavailable);

        return HandleResult(await Mediator.Send(
            new InitializePaymentCommand(request.Amount), cancellationToken));
    }

    [HttpPost("verify")]
    public async Task<IActionResult> Verify(
        [FromBody] VerifyWalletPaymentRequest request,
        CancellationToken cancellationToken)
    {
        if (!PaymentsEnabled)
            return StatusCode(StatusCodes.Status503ServiceUnavailable);

        return HandleResult(await Mediator.Send(
            new VerifyPaymentCommand(request.Reference), cancellationToken));
    }

    [AllowAnonymous]
    [HttpPost("webhook/paystack")]
    [RequestSizeLimit(PaystackWebhookParser.MaxPayloadBytes)]
    public async Task<IActionResult> PaystackWebhook(CancellationToken cancellationToken)
    {
        if (!PaymentsEnabled)
            return StatusCode(StatusCodes.Status503ServiceUnavailable);

        if (Request.ContentLength > PaystackWebhookParser.MaxPayloadBytes)
            return StatusCode(StatusCodes.Status413PayloadTooLarge);

        var signatures = Request.Headers["x-paystack-signature"];
        if (signatures.Count != 1 || string.IsNullOrWhiteSpace(signatures[0]))
            return Unauthorized();

        // Leave time to return a non-200 response before the sender's timeout.
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(20));

        try
        {
            // Check the actual byte count as well as Content-Length, which may be absent.
            using var body = new MemoryStream();
            var buffer = new byte[8192];
            int read;
            while ((read = await Request.Body.ReadAsync(buffer.AsMemory(), timeout.Token)) != 0)
            {
                if (body.Length + read > PaystackWebhookParser.MaxPayloadBytes)
                    return StatusCode(StatusCodes.Status413PayloadTooLarge);
                await body.WriteAsync(buffer.AsMemory(0, read), timeout.Token);
            }

            var result = await Mediator.Send(
                new ProcessPaystackWebhookCommand(body.ToArray(), signatures[0]!),
                timeout.Token);

            if (result.IsSuccess)
                return Ok();

            // Never return 200 for a charge that couldn't be verified or credited.
            return result.ErrorType switch
            {
                ErrorType.Unauthorized => Unauthorized(),
                ErrorType.Validation => BadRequest(),
                _ => StatusCode(StatusCodes.Status503ServiceUnavailable)
            };
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested && timeout.IsCancellationRequested)
        {
            return StatusCode(StatusCodes.Status503ServiceUnavailable);
        }
    }

    [HttpGet("admin/pending")]
    public async Task<IActionResult> GetPendingPayments(
    [FromQuery] int page = 1,
    [FromQuery] int pageSize = 50,
    CancellationToken cancellationToken = default)
    {
        if (!PaymentsEnabled)
            return StatusCode(StatusCodes.Status503ServiceUnavailable);

        return HandleResult(await Mediator.Send(
            new GetPendingPaymentReviewsQuery(page, pageSize),
            cancellationToken));
    }

    [HttpPost("admin/reconcile")]
    public async Task<IActionResult> ReconcilePayment(
    [FromBody] AdminReconcilePaymentRequest request,
    CancellationToken cancellationToken)
    {
        if (!PaymentsEnabled)
            return StatusCode(StatusCodes.Status503ServiceUnavailable);

        return HandleResult(await Mediator.Send(
            new AdminReconcilePaymentCommand(request.Reference, request.Reason),
            cancellationToken));
    }

    [HttpGet("admin/reconciliations/{reference}")]
    public async Task<IActionResult> GetReconciliationHistory(
    string reference,
    [FromQuery] int page = 1,
    [FromQuery] int pageSize = 50,
    CancellationToken cancellationToken = default)
    {
        if (!PaymentsEnabled)
            return StatusCode(StatusCodes.Status503ServiceUnavailable);

        return HandleResult(await Mediator.Send(
            new GetPaymentReconciliationHistoryQuery(reference, page, pageSize),
            cancellationToken
        ));
    }

    [HttpGet("admin/automatic-reviews")]
    public async Task<IActionResult> GetAutomaticPaymentReviews(
    [FromQuery] int page = 1,
    [FromQuery] int pageSize = 50,
    [FromQuery] Guid? userId = null,
    CancellationToken cancellationToken = default)
    {
        if (!PaymentsEnabled)
            return StatusCode(StatusCodes.Status503ServiceUnavailable);

        return HandleResult(await Mediator.Send(
            new GetAutomaticPaymentReviewQueueQuery(page, pageSize, userId),
            cancellationToken));
    }
}


