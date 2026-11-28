using Errandy.Api.Extensions;
using Errandy.Api.Storage;
using Errandy.Application.Features.Errands.AcceptErrand;
using Errandy.Application.Features.Errands.AdminForceComplete;
using Errandy.Application.Features.Errands.CancelErrand;
using Errandy.Application.Features.Errands.ConfirmCompletion;
using Errandy.Application.Features.Errands.CreateErrand;
using Errandy.Application.Features.Errands.GetErrandById;
using Errandy.Application.Features.Errands.GetMyErrands;
using Errandy.Application.Features.Errands.GetNearbyErrands;
using Errandy.Application.Features.Errands.GetProofFile;
using Errandy.Application.Features.Errands.ProposePriceAdjustment;
using Errandy.Application.Features.Errands.RespondToPriceAdjustment;
using Errandy.Application.Features.Errands.StartErrand;
using Errandy.Application.Features.Errands.UploadProof;
using Errandy.Application.Interfaces;
using Errandy.Domain.Enums;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Errandy.Api.Filters;
namespace Errandy.Api.Controllers;

[ApiController]
[Route("api/errands")]
[ConflictExceptionFilter]
public class ErrandsController : ControllerBase
{
    private readonly IMediator _mediator;

    public ErrandsController(IMediator mediator)
    {
        _mediator = mediator;
    }

    /// <summary>Customer creates an errand. The customer is the logged-in user.</summary>
    [HttpPost]
    [Authorize(Roles = "Customer")]
    public async Task<IActionResult> Create([FromBody] CreateErrandRequestBody body, CancellationToken cancellationToken)
    {
        var errandId = await _mediator.Send(new CreateErrandCommand
        {
            CustomerId = User.GetUserId(),
            Category = body.Category,
            Description = body.Description,
            EstimatedCost = body.EstimatedCost,
            PickupLatitude = body.PickupLatitude,
            PickupLongitude = body.PickupLongitude,
            TimePreference = body.TimePreference,
            ScheduledDeadline = body.ScheduledDeadline
        }, cancellationToken);

        return CreatedAtAction(nameof(GetById), new { id = errandId }, new { id = errandId });
    }


    /// <summary>Runner browses open errands nearby (default 5km).</summary>
    [HttpGet("nearby")]
    [Authorize(Roles = "Runner")]
    public async Task<IActionResult> GetNearby(
        [FromQuery] double lat,
        [FromQuery] double lng,
        [FromQuery] double radiusKm = 5.0,
        [FromQuery] ErrandCategory? category = null,
        [FromQuery] NearbyErrandsSortBy sortBy = NearbyErrandsSortBy.Distance,
        CancellationToken cancellationToken = default)
    {
        var result = await _mediator.Send(new GetNearbyErrandsQuery
        {
            RunnerLatitude = lat,
            RunnerLongitude = lng,
            RadiusKm = radiusKm,
            Category = category,
            SortBy = sortBy
        }, cancellationToken);

        return Ok(result);
    }

    /// <summary>The logged-in user's own errands (as customer or as runner).</summary>
    [HttpGet("mine")]
    [Authorize]
    public async Task<IActionResult> GetMine(
        [FromQuery] bool asCustomer = true,
        [FromQuery] ErrandStatus? status = null,
        CancellationToken cancellationToken = default)
    {
        var result = await _mediator.Send(new GetMyErrandsQuery
        {
            UserId = User.GetUserId(),
            AsCustomer = asCustomer,
            StatusFilter = status
        }, cancellationToken);

        return Ok(result);
    }

    [HttpGet("{id:guid}")]
    [Authorize]
    public async Task<IActionResult> GetById(Guid id, CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(
            new GetErrandByIdQuery { ErrandId = id, RequestingUserId = User.GetUserId() },
            cancellationToken);

        return Ok(result);
    }

    /// <summary>Runner accepts an errand. The runner is the logged-in user.</summary>
    [HttpPost("{id:guid}/accept")]
    [Authorize(Roles = "Runner")]
    public async Task<IActionResult> Accept(Guid id, CancellationToken cancellationToken)
    {
        await _mediator.Send(new AcceptErrandCommand { ErrandId = id, RunnerId = User.GetUserId() }, cancellationToken);
        return NoContent();
    }

    [HttpPost("{id:guid}/start")]
    [Authorize(Roles = "Runner")]
    public async Task<IActionResult> Start(Guid id, CancellationToken cancellationToken)
    {
        await _mediator.Send(new StartErrandCommand { ErrandId = id, RunnerId = User.GetUserId() }, cancellationToken);
        return NoContent();
    }

    // Two photos of up to 8 MB each, plus form overhead.
    private const long MaxProofUploadBytes = 18L * 1024 * 1024;

    /// <summary>Runner uploads proof as real files: a photo (required) and a receipt (optional).</summary>
    [HttpPost("{id:guid}/proof")]
    [Authorize(Roles = "Runner")]
    [Consumes("multipart/form-data")]
    [RequestSizeLimit(MaxProofUploadBytes)]
    [RequestFormLimits(MultipartBodyLengthLimit = MaxProofUploadBytes)]
    public async Task<IActionResult> UploadProof(Guid id, [FromForm] UploadProofForm form, CancellationToken cancellationToken)
    {
        await using var imageStream = form.Image.OpenReadStream();
        await using var receiptStream = form.Receipt?.OpenReadStream();

        await _mediator.Send(new UploadProofCommand
        {
            ErrandId = id,
            RunnerId = User.GetUserId(),
            ImageContent = imageStream,
            ReceiptContent = receiptStream,
            FinalCost = form.FinalCost
        }, cancellationToken);

        return NoContent();
    }

    /// <summary>Private download of the proof photo. Only the customer, the runner or an admin.</summary>
    [HttpGet("{id:guid}/proof/image")]
    [Authorize]
    public Task<IActionResult> GetProofImage(Guid id, CancellationToken cancellationToken) =>
        ServeProofFile(id, ProofFileKind.Image, cancellationToken);

    /// <summary>Private download of the receipt, if one was uploaded.</summary>
    [HttpGet("{id:guid}/proof/receipt")]
    [Authorize]
    public Task<IActionResult> GetProofReceipt(Guid id, CancellationToken cancellationToken) =>
        ServeProofFile(id, ProofFileKind.Receipt, cancellationToken);

    private async Task<IActionResult> ServeProofFile(Guid id, ProofFileKind kind, CancellationToken cancellationToken)
    {
        var file = await _mediator.Send(new GetProofFileQuery
        {
            ErrandId = id,
            RequestingUserId = User.GetUserId(),
            IsAdmin = User.IsInRole("Admin"),
            Kind = kind
        }, cancellationToken);

        Response.Headers["X-Content-Type-Options"] = "nosniff";
        Response.Headers["Cache-Control"] = "private, max-age=3600";
        return File(file.Content, file.ContentType);
    }

    /// <summary>Customer confirms completion, which triggers payment release.</summary>
    [HttpPost("{id:guid}/confirm")]
    [Authorize(Roles = "Customer")]
    public async Task<IActionResult> Confirm(Guid id, CancellationToken cancellationToken)
    {
        await _mediator.Send(new ConfirmCompletionCommand { ErrandId = id, CustomerId = User.GetUserId() }, cancellationToken);
        return NoContent();
    }

    /// <summary>Customer or assigned runner cancels (penalty rules apply, PRD 13).</summary>
    [HttpPost("{id:guid}/cancel")]
    [Authorize]
    public async Task<IActionResult> Cancel(Guid id, CancellationToken cancellationToken)
    {
        await _mediator.Send(new CancelErrandCommand { ErrandId = id, RequestingUserId = User.GetUserId() }, cancellationToken);
        return NoContent();
    }

    [HttpPost("{id:guid}/price-adjustment")]
    [Authorize(Roles = "Runner")]
    public async Task<IActionResult> ProposePriceAdjustment(Guid id, [FromBody] ProposePriceAdjustmentRequestBody body, CancellationToken cancellationToken)
    {
        await _mediator.Send(new ProposePriceAdjustmentCommand
        {
            ErrandId = id,
            RunnerId = User.GetUserId(),
            NewCost = body.NewCost,
            Reason = body.Reason
        }, cancellationToken);

        return NoContent();
    }

    [HttpPost("{id:guid}/price-adjustment/respond")]
    [Authorize(Roles = "Customer")]
    public async Task<IActionResult> RespondToPriceAdjustment(Guid id, [FromBody] RespondToPriceAdjustmentRequestBody body, CancellationToken cancellationToken)
    {
        await _mediator.Send(new RespondToPriceAdjustmentCommand
        {
            ErrandId = id,
            CustomerId = User.GetUserId(),
            Approve = body.Approve
        }, cancellationToken);

        return NoContent();
    }

    /// <summary>Admin override: force-complete a stuck errand (PRD 19).</summary>
    [HttpPost("{id:guid}/force-complete")]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> ForceComplete(Guid id, [FromBody] AdminForceCompleteRequestBody body, CancellationToken cancellationToken)
    {
        await _mediator.Send(new AdminForceCompleteErrandCommand
        {
            ErrandId = id,
            AdminId = User.GetUserId(),
            Reason = body.Reason
        }, cancellationToken);

        return NoContent();
    }
}

// Request bodies. None of them contain a user ID: who is calling comes from the login token.
public record CreateErrandRequestBody(
    ErrandCategory Category,
    string Description,
    decimal EstimatedCost,
    double PickupLatitude,
    double PickupLongitude,
    ErrandTimePreference TimePreference = ErrandTimePreference.Asap,
    DateTime? ScheduledDeadline = null);

public class UploadProofForm
{
    /// <summary>Photo of the completed task (JPEG, PNG or WebP, up to 8 MB).</summary>
    public IFormFile Image { get; set; } = null!;

    /// <summary>Optional photo of the receipt (JPEG, PNG or WebP, up to 8 MB).</summary>
    public IFormFile? Receipt { get; set; }

    public decimal FinalCost { get; set; }
}
public record ProposePriceAdjustmentRequestBody(decimal NewCost, string Reason);
public record RespondToPriceAdjustmentRequestBody(bool Approve);
public record AdminForceCompleteRequestBody(string Reason);