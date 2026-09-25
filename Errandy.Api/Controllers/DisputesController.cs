using Errandy.Application.Features.Disputes.CreateDispute;
using Errandy.Application.Features.Disputes.ResolveDispute;
using Errandy.Domain.Enums;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Errandy.Api.Controllers;

[ApiController]
[Route("api/disputes")]
[Authorize]
public class DisputesController : ControllerBase
{
    private readonly IMediator _mediator;

    public DisputesController(IMediator mediator)
    {
        _mediator = mediator;
    }

    /// <summary>Customer or runner raises a dispute on an errand (PRD 12.1).</summary>
    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateDisputeCommand command, CancellationToken cancellationToken)
    {
        var disputeId = await _mediator.Send(command, cancellationToken);
        return CreatedAtAction(nameof(Create), new { id = disputeId }, new { id = disputeId });
    }

    /// <summary>Admin resolves a dispute — full payment / partial refund / penalty (PRD 12.3).</summary>
    [HttpPost("{id:guid}/resolve")]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> Resolve(Guid id, [FromBody] ResolveDisputeRequestBody body, CancellationToken cancellationToken)
    {
        await _mediator.Send(new ResolveDisputeCommand
        {
            DisputeId = id,
            AdminId = body.AdminId,
            Resolution = body.Resolution,
            RefundToCustomerAmount = body.RefundToCustomerAmount,
            ReleaseToRunnerAmount = body.ReleaseToRunnerAmount,
            Notes = body.Notes
        }, cancellationToken);

        return NoContent();
    }
}

/// <summary>Placeholder until ICurrentUserService supplies AdminId from claims.</summary>
public record ResolveDisputeRequestBody(
    Guid AdminId,
    DisputeResolutionType Resolution,
    decimal? RefundToCustomerAmount,
    decimal? ReleaseToRunnerAmount,
    string? Notes);