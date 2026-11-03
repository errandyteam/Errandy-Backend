using Errandy.Api.Contracts;
using Errandy.Application.Common.DTOs;
using Errandy.Application.Features.Users.Commands.UpdateProfile;
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

    /// <summary>Updates the authenticated user's profile (name and phone).</summary>
    [HttpPut("me")]
    public async Task<IActionResult> UpdateMyProfile(
     [FromBody] UpdateProfileRequest request,
     CancellationToken ct)
    {
        var userId = CurrentUser.UserId;
        if (userId is null) return Unauthorized();

        var command = new UpdateProfileCommand(
            userId.Value,
            request.FullName,
            request.PhoneNumber);

        var result = await Mediator.Send(command, ct);

        if (!result.IsSuccess)
        {
            return HandleResult(result);
        }

        return Ok(new
        {
            message = "Profile updated successfully",
            data = result.Value
        });
    }
}