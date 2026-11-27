using Errandy.Api.Contracts;
using Errandy.Application.Features.Admin.Commands.ActivateUser;
using Errandy.Application.Features.Admin.Commands.SuspendUser;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Errandy.Api.Controllers;

[Authorize(Roles = "Admin")]
[Route("api/admin")]
[ApiController]
[Produces("application/json")]
public class AdminController : ApiControllerBase
{
    /// <summary>Suspends/deactivates a user.</summary>
    [HttpPost("users/{userId:guid}/suspend")]
    public async Task<IActionResult> SuspendUser(
        Guid userId,
        [FromBody] SuspendUserRequest request,
        CancellationToken ct)
    {
        var command = new SuspendUserCommand(userId, request.Reason);
        return HandleResult(await Mediator.Send(command, ct));
    }

    /// <summary>Activates/reinstates a suspended user.</summary>
    [HttpPost("users/{userId:guid}/activate")]
    public async Task<IActionResult> ActivateUser(Guid userId, CancellationToken ct)
    {
        return HandleResult(await Mediator.Send(new ActivateUserCommand(userId), ct));
    }
}