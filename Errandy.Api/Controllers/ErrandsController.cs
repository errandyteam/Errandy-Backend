using Errandy.Application.Features.Errands.AcceptErrand;
using Errandy.Application.Features.Errands.AdminForceComplete;
using Errandy.Application.Features.Errands.CancelErrand;
using Errandy.Application.Features.Errands.ConfirmCompletion;
using Errandy.Application.Features.Errands.CreateErrand;
using Errandy.Application.Features.Errands.GetErrandById;
using Errandy.Application.Features.Errands.GetMyErrands;
using Errandy.Application.Features.Errands.GetNearbyErrands;
using Errandy.Application.Features.Errands.ProposePriceAdjustment;
using Errandy.Application.Features.Errands.RespondToPriceAdjustment;
using Errandy.Application.Features.Errands.StartErrand;
using Errandy.Application.Features.Errands.UploadProof;
using Errandy.Domain.Enums;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Errandy.Api.Controllers;

[ApiController]
[Route("api/errands")]
public class ErrandsController : ControllerBase
{
    private readonly IMediator _mediator;

    public ErrandsController(IMediator mediator)
    {
        _mediator = mediator;
    }

    /// <summary>
    /// Customer creates a new errand. Locks estimated cost in escrow immediately.
    /// </summary>
    [HttpPost]
    [Authorize(Roles = "Customer")]
    public async Task<IActionResult> Create([FromBody] CreateErrandCommand command, CancellationToken cancellationToken)
    {
        var errandId = await _mediator.Send(command, cancellationToken);
        return CreatedAtAction(nameof(Create), new { id = errandId }, new { id = errandId });
    }

    /// <summary>
    /// Runner browses errands within radius (default 5km per PRD).
    /// </summary>
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
        var result = await _mediator.Send(
            new GetNearbyErrandsQuery
            {
                RunnerLatitude = lat,
                RunnerLongitude = lng,
                RadiusKm = radiusKm,
                Category = category,
                SortBy = sortBy
            },
            cancellationToken);
        return Ok(result);
    }

    /// <summary>
    /// Runner accepts an available errand.
    /// </summary>
    [HttpPost("{id:guid}/accept")]
    [Authorize(Roles = "Runner")]
    public async Task<IActionResult> Accept(Guid id, [FromBody] AcceptErrandRequestBody body, CancellationToken cancellationToken)
    {
        await _mediator.Send(new AcceptErrandCommand { ErrandId = id, RunnerId = body.RunnerId }, cancellationToken);
        return NoContent();
    }

    [HttpGet("{id:guid}")]
    [Authorize]
    public async Task<IActionResult> GetById(Guid id, [FromQuery] Guid requestingUserId, CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(
            new GetErrandByIdQuery { ErrandId = id, RequestingUserId = requestingUserId },
            cancellationToken);
        return Ok(result);
    }


    /// <summary>
    /// Runner marks an accepted errand as in progress.
    /// </summary>
    [HttpPost("{id:guid}/start")]
    [Authorize(Roles = "Runner")]
    public async Task<IActionResult> Start(Guid id, [FromBody] AcceptErrandRequestBody body, CancellationToken cancellationToken)
    {
        await _mediator.Send(new StartErrandCommand { ErrandId = id, RunnerId = body.RunnerId }, cancellationToken);
        return NoContent();
    }

    /// <summary>
    /// Runner uploads proof of completion (photo + optional receipt) and states final cost.
    /// </summary>
    [HttpPost("{id:guid}/proof")]
    [Authorize(Roles = "Runner")]
    public async Task<IActionResult> UploadProof(Guid id, [FromBody] UploadProofRequestBody body, CancellationToken cancellationToken)
    {
        await _mediator.Send(new UploadProofCommand
        {
            ErrandId = id,
            RunnerId = body.RunnerId,
            ImageUrl = body.ImageUrl,
            ReceiptUrl = body.ReceiptUrl,
            FinalCost = body.FinalCost
        }, cancellationToken);
        return NoContent();
    }


    /// <summary>
    /// Customer confirms completion — this is what triggers escrow release to the runner.
    /// </summary>
    [HttpPost("{id:guid}/confirm")]
    [Authorize(Roles = "Customer")]
    public async Task<IActionResult> Confirm(Guid id, [FromBody] ConfirmCompletionRequestBody body, CancellationToken cancellationToken)
    {
        await _mediator.Send(new ConfirmCompletionCommand { ErrandId = id, CustomerId = body.CustomerId }, cancellationToken);
        return NoContent();
    }

    /// <summary>
    /// Customer or runner cancels — penalty rules depend on who's cancelling and
    /// what stage the errand is at (PRD 13).
    /// </summary>
    [HttpPost("{id:guid}/cancel")]
    [Authorize]
    public async Task<IActionResult> Cancel(Guid id, [FromBody] CancelErrandRequestBody body, CancellationToken cancellationToken)
    {
        await _mediator.Send(new CancelErrandCommand { ErrandId = id, RequestingUserId = body.RequestingUserId }, cancellationToken);
        return NoContent();
    }
    /// <summary>Runner proposes a cost change mid-task (PRD 7).</summary>
    [HttpPost("{id:guid}/price-adjustment")]
    [Authorize(Roles = "Runner")]
    public async Task<IActionResult> ProposePriceAdjustment(Guid id, [FromBody] ProposePriceAdjustmentRequestBody body, CancellationToken cancellationToken)
    {
        await _mediator.Send(new ProposePriceAdjustmentCommand
        {
            ErrandId = id,
            RunnerId = body.RunnerId,
            NewCost = body.NewCost,
            Reason = body.Reason
        }, cancellationToken);
        return NoContent();
    }

    /// <summary>Customer approves or rejects a pending price adjustment.</summary>
    [HttpPost("{id:guid}/price-adjustment/respond")]
    [Authorize(Roles = "Customer")]
    public async Task<IActionResult> RespondToPriceAdjustment(Guid id, [FromBody] RespondToPriceAdjustmentRequestBody body, CancellationToken cancellationToken)
    {
        await _mediator.Send(new RespondToPriceAdjustmentCommand
        {
            ErrandId = id,
            CustomerId = body.CustomerId,
            Approve = body.Approve
        }, cancellationToken);
        return NoContent();
    }

    [HttpPost("{id:guid}/force-complete")]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> ForceComplete(Guid id, [FromBody] AdminForceCompleteRequestBody body, CancellationToken cancellationToken)
    {
        await _mediator.Send(new AdminForceCompleteErrandCommand
        {
            ErrandId = id,
            AdminId = body.AdminId,
            Reason = body.Reason
        }, cancellationToken);
        return NoContent();
    }

    [HttpGet("mine")]
    [Authorize]
    public async Task<IActionResult> GetMine(
    [FromQuery] Guid userId,
    [FromQuery] bool asCustomer = true,
    [FromQuery] Domain.Enums.ErrandStatus? status = null,
    CancellationToken cancellationToken = default)
    {
        var result = await _mediator.Send(
            new GetMyErrandsQuery { UserId = userId, AsCustomer = asCustomer, StatusFilter = status },
            cancellationToken);
        return Ok(result);
    }
}

/// <summary>
/// Once Engineer A's ICurrentUserService is wired in, these IDs should come
/// from the authenticated user's claims instead of the request body — these
/// body-based versions are placeholders so endpoints are testable today.
/// </summary>
public record AcceptErrandRequestBody(Guid RunnerId);
public record UploadProofRequestBody(Guid RunnerId, string ImageUrl, string? ReceiptUrl, decimal FinalCost);
public record ConfirmCompletionRequestBody(Guid CustomerId);
public record CancelErrandRequestBody(Guid RequestingUserId);
public record ProposePriceAdjustmentRequestBody(Guid RunnerId, decimal NewCost, string Reason);
public record RespondToPriceAdjustmentRequestBody(Guid CustomerId, bool Approve);
public record AdminForceCompleteRequestBody(Guid AdminId, string Reason);
