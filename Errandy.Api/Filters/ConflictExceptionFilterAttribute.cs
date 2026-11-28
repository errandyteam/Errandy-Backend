using Errandy.Domain.Exceptions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.EntityFrameworkCore;

namespace Errandy.Api.Filters;

/// <summary>
/// Turns "someone else got there first" into a clear 409 Conflict instead of a 500.
/// Covers two cases: two people changing the same errand at the same moment, and an action
/// that isn't allowed in the errand's current state (e.g. accepting an already-accepted errand).
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
public sealed class ConflictExceptionFilterAttribute : ExceptionFilterAttribute
{
    public override void OnException(ExceptionContext context)
    {
        string? detail = context.Exception switch
        {
            DbUpdateConcurrencyException => "This errand was changed by someone else at the same moment. Reload it and try again.",
            InvalidErrandStateException ex => ex.Message,
            _ => null
        };

        if (detail is null)
            return;

        var problem = new ProblemDetails
        {
            Title = "Conflict",
            Status = StatusCodes.Status409Conflict,
            Detail = detail
        };

        context.Result = new ObjectResult(problem)
        {
            StatusCode = StatusCodes.Status409Conflict,
            ContentTypes = { "application/problem+json" }
        };
        context.ExceptionHandled = true;
    }
}