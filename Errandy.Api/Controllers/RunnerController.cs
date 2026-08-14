using Errandy.Api.Contracts;
using Errandy.Application.Common.DTOs;
using Errandy.Application.Features.Runners.Commands.BecomeRunner;
using Errandy.Application.Features.Runners.Commands.ReviewKyc;
using Errandy.Application.Features.Runners.Commands.SubmitKyc;
using Errandy.Application.Features.Runners.Queries.GetPendingKyc;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
namespace Errandy.Api.Controllers;

[Authorize]
public class RunnerController : ApiControllerBase
{
    /// <summary>Upgrades the current user to a runner (creates a pending profile).</summary>
    [HttpPost("become")]
    [ProducesResponseType(typeof(RunnerProfileDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> BecomeRunner(CancellationToken ct)
    {
        var userId = CurrentUser.UserId;
        if (userId is null) return Unauthorized();
        return HandleResult(await Mediator.Send(new BecomeRunnerCommand(userId.Value), ct));
    }
    /// <summary>Submits (or resubmits) KYC documents for review. Runner only.</summary>
    [HttpPost("kyc")]
    [Authorize(Roles = "Runner")]
    [ProducesResponseType(typeof(RunnerProfileDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> SubmitKyc(
        [FromBody] SubmitKycRequest request, CancellationToken ct)
    {
        var userId = CurrentUser.UserId;
        if (userId is null) return Unauthorized();
        var command = new SubmitKycCommand(
            userId.Value, request.DocumentType, request.DocumentNumber, request.DocumentImageUrl);
        return HandleResult(await Mediator.Send(command, ct));
    }
    /// <summary>Lists runner profiles awaiting KYC review. Admin only.</summary>
    [HttpGet("kyc/pending")]
    [Authorize(Roles = "Admin")]
    [ProducesResponseType(typeof(IReadOnlyList<RunnerProfileDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetPendingKyc(CancellationToken ct)
        => HandleResult(await Mediator.Send(new GetPendingKycQuery(), ct));
    /// <summary>Approves or rejects a runner's KYC submission. Admin only.</summary>
    [HttpPost("kyc/{runnerProfileId:guid}/review")]
    [Authorize(Roles = "Admin")]
    [ProducesResponseType(typeof(RunnerProfileDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> ReviewKyc(
        Guid runnerProfileId,
        [FromBody] ReviewKycRequest request,
        CancellationToken ct)
    {
        var adminId = CurrentUser.UserId;
        if (adminId is null) return Unauthorized();
        var command = new ReviewKycCommand(
            adminId.Value, runnerProfileId, request.Approve, request.RejectionReason);
        return HandleResult(await Mediator.Send(command, ct));
    }
}