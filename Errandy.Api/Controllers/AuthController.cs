using Errandy.Api.Contracts;
using Errandy.Application.Common.DTOs;
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
    [ProducesResponseType(typeof(AuthResponseDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Register(
        [FromBody] RegisterRequest request, CancellationToken ct)
    {
        var command = new RegisterUserCommand(
            request.FullName, request.Email, request.PhoneNumber, request.Password);
        return HandleResult(await Mediator.Send(command, ct));
    }
    /// <summary>Authenticates a user and returns a JWT.</summary>
    [HttpPost("login")]
    [ProducesResponseType(typeof(AuthResponseDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> Login(
        [FromBody] LoginRequest request, CancellationToken ct)
    {
        var command = new LoginUserCommand(request.Email, request.Password);
        return HandleResult(await Mediator.Send(command, ct));
    }
}
