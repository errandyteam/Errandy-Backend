using Errandy.Api.Extensions;
using Errandy.Application.Features.Disputes.AddDisputeEvidence;
using Errandy.Application.Features.Disputes.CreateDispute;
using Errandy.Application.Features.Disputes.GetDisputeById;
using Errandy.Application.Features.Disputes.GetOpenDisputes;
using Errandy.Application.Features.Disputes.RejectDispute;
using Errandy.Application.Features.Disputes.ResolveDispute;
using Errandy.Domain.Enums;
using MediatR;
using Errandy.Api.Filters;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Errandy.Api.Controllers;

[ApiController]
[Route("api/disputes")]
[Authorize]
[ConflictExceptionFilter]
public class DisputesController : ControllerBase
{
    private readonly IMediator _mediator;

    public DisputesController(IMediator mediator)
    {
        _mediator = mediator;
    }

    /// <summary>Customer or runner raises a dispute (PRD 12.1).</summary>
    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateDisputeRequestBody body, CancellationToken cancellationToken)
    {
        var disputeId = await _mediator.Send(new CreateDisputeCommand
        {
            ErrandId = body.ErrandId,
            RaisedBy = User.GetUserId(),
            Reason = body.Reason,
            Description = body.Description,
            EvidenceUrls = body.EvidenceUrls ?? new List<string>()
        }, cancellationToken);

        return CreatedAtAction(nameof(GetById), new { id = disputeId }, new { id = disputeId });
    }

    /// <summary>Admin work queue: unresolved disputes, oldest first, with an SLA flag.</summary>
    [HttpGet("open")]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> GetOpen(CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(new GetOpenDisputesQuery(), cancellationToken);
        return Ok(result);
    }

    /// <summary>View one dispute. Admins, or the customer/runner on that errand.</summary>
    [HttpGet("{id:guid}")]
    public async Task<IActionResult> GetById(Guid id, CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(new GetDisputeByIdQuery
        {
            DisputeId = id,
            RequestingUserId = User.GetUserId(),
            IsAdmin = User.IsInRole("Admin")
        }, cancellationToken);

        return Ok(result);
    }

    /// <summary>Customer or runner adds more evidence to an open dispute.</summary>
    [HttpPost("{id:guid}/evidence")]
    public async Task<IActionResult> AddEvidence(Guid id, [FromBody] AddEvidenceRequestBody body, CancellationToken cancellationToken)
    {
        await _mediator.Send(new AddDisputeEvidenceCommand
        {
            DisputeId = id,
            UserId = User.GetUserId(),
            EvidenceUrls = body.EvidenceUrls ?? new List<string>()
        }, cancellationToken);

        return NoContent();
    }

    /// <summary>Admin resolves a dispute with a money decision (PRD 12.3).</summary>
    [HttpPost("{id:guid}/resolve")]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> Resolve(Guid id, [FromBody] ResolveDisputeRequestBody body, CancellationToken cancellationToken)
    {
        await _mediator.Send(new ResolveDisputeCommand
        {
            DisputeId = id,
            AdminId = User.GetUserId(),
            Resolution = body.Resolution,
            RefundToCustomerAmount = body.RefundToCustomerAmount,
            ReleaseToRunnerAmount = body.ReleaseToRunnerAmount,
            Notes = body.Notes
        }, cancellationToken);

        return NoContent();
    }

    /// <summary>Admin dismisses a dispute. A reason is required.</summary>
    [HttpPost("{id:guid}/reject")]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> Reject(Guid id, [FromBody] RejectDisputeRequestBody body, CancellationToken cancellationToken)
    {
        await _mediator.Send(new RejectDisputeCommand
        {
            DisputeId = id,
            AdminId = User.GetUserId(),
            Notes = body.Notes
        }, cancellationToken);

        return NoContent();
    }
}

public record CreateDisputeRequestBody(
    Guid ErrandId,
    DisputeReason Reason,
    string? Description,
    List<string>? EvidenceUrls);

public record AddEvidenceRequestBody(List<string>? EvidenceUrls);

public record ResolveDisputeRequestBody(
    DisputeResolutionType Resolution,
    decimal? RefundToCustomerAmount,
    decimal? ReleaseToRunnerAmount,
    string? Notes);

public record RejectDisputeRequestBody(string Notes);