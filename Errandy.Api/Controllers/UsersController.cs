using Errandy.Application.Common.DTOs;
using Errandy.Application.Features.Users.Queries.GetCurrentUser;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
namespace Errandy.Api.Controllers;

[Authorize]
public class UsersController : ApiControllerBase
{
    /// <summary>Returns the profile of the currently authenticated user.</summary>
    [HttpGet("me")]
    [ProducesResponseType(typeof(UserDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Me(CancellationToken ct)
    {
        var userId = CurrentUser.UserId;
        if (userId is null)
            return Unauthorized();
        return HandleResult(await Mediator.Send(new GetCurrentUserQuery(userId.Value), ct));
    }
}