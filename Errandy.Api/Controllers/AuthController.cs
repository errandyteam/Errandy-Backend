using Errandy.Api.Contracts;
using Errandy.Application.Common.DTOs;
using Errandy.Application.Features.Auth.Commands.ChangePassword;
using Errandy.Application.Features.Auth.Commands.LoginUser;
using Errandy.Application.Features.Auth.Commands.RegisterUser;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
namespace Errandy.Api.Controllers;

[AllowAnonymous]
public class AuthController : ApiControllerBase
{
    /// <summary>Registers a new customer account and returns a JWT.</summary>
    [HttpPost("register")]
    public async Task<IActionResult> Register(
        [FromBody] RegisterRequest request, CancellationToken ct)
    {
        var command = new RegisterUserCommand(
            request.FullName, request.Email, request.PhoneNumber, request.Password);
        return HandleResult(await Mediator.Send(command, ct));
    }
    /// <summary>Authenticates a user and returns a JWT.</summary>
    [HttpPost("login")]
    public async Task<IActionResult> Login(
        [FromBody] LoginRequest request, CancellationToken ct)
    {
        var command = new LoginUserCommand(request.Email, request.Password);
        return HandleResult(await Mediator.Send(command, ct));
    }

    /// <summary>Changes the authenticated user's password.</summary>
    [HttpPost("change-password")]
    [Authorize]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> ChangePassword(
        [FromBody] ChangePasswordRequest request, CancellationToken ct)
    {
        var userId = CurrentUser.UserId;
        if (userId is null) return Unauthorized();

        var command = new ChangePasswordCommand(
            userId.Value, request.CurrentPassword, request.NewPassword);

        var result = await Mediator.Send(command, ct);

        if (!result.IsSuccess)
        {
            return HandleResult(result);
        }

        return Ok(new
        {
            message = "Password changed successfully"
        });


    }
}
