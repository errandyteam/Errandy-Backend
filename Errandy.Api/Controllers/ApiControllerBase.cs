using Errandy.Application.Common.Interfaces;
using Errandy.Application.Common.Results;
using MediatR;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Mvc;
namespace Errandy.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
[Produces("application/json")]
public abstract class ApiControllerBase : ControllerBase
{
    private ISender? _mediator;
    private ICurrentUser? _currentUser;
    protected ISender Mediator =>
        _mediator ??= HttpContext.RequestServices.GetRequiredService<ISender>();
    protected ICurrentUser CurrentUser =>
        _currentUser ??= HttpContext.RequestServices.GetRequiredService<ICurrentUser>();
    /// <summary>
    /// Maps an application Result to the appropriate HTTP response.
    /// </summary>
    protected IActionResult HandleResult<T>(Result<T> result)
    {
        if (result.IsSuccess)
            return Ok(result.Value);
        return MapError(result.Error!, result.ErrorType);
    }
    protected IActionResult HandleResult(Result result)
    {
        if (result.IsSuccess)
            return NoContent();
        return MapError(result.Error!, result.ErrorType);
    }
    private IActionResult MapError(string error, ErrorType type)
    {
        var problem = new ProblemDetails
        {
            Title = type.ToString(),
            Detail = error,
            Status = type switch
            {
                ErrorType.NotFound => StatusCodes.Status404NotFound,
                ErrorType.Conflict => StatusCodes.Status409Conflict,
                ErrorType.Unauthorized => StatusCodes.Status401Unauthorized,
                ErrorType.Forbidden => StatusCodes.Status403Forbidden,
                _ => StatusCodes.Status400BadRequest
            }
        };
        return StatusCode(problem.Status!.Value, problem);
    }
}
