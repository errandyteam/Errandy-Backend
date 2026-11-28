using Errandy.Api.Filters;
using Errandy.Domain.Enums;
using Errandy.Domain.Exceptions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Errandy.Tests.Filters;

public class ConflictExceptionFilterTests
{
    private static ExceptionContext ContextFor(Exception exception) =>
        new(new ActionContext(new DefaultHttpContext(), new RouteData(), new ActionDescriptor()), new List<IFilterMetadata>())
        {
            Exception = exception
        };

    [Fact]
    public void AConcurrencyConflict_BecomesA409()
    {
        var context = ContextFor(new DbUpdateConcurrencyException("someone else got there first"));

        new ConflictExceptionFilterAttribute().OnException(context);

        var result = Assert.IsType<ObjectResult>(context.Result);
        Assert.Equal(409, result.StatusCode);
        Assert.True(context.ExceptionHandled);
    }

    [Fact]
    public void AnInvalidStateChange_BecomesA409_WithTheReasonShown()
    {
        var context = ContextFor(new InvalidErrandStateException(ErrandStatus.Accepted, "Accept"));

        new ConflictExceptionFilterAttribute().OnException(context);

        var result = Assert.IsType<ObjectResult>(context.Result);
        Assert.Equal(409, result.StatusCode);
        var problem = Assert.IsType<ProblemDetails>(result.Value);
        Assert.Contains("Accepted", problem.Detail);
    }

    [Fact]
    public void AnyOtherException_IsLeftForTheNormalErrorHandling()
    {
        var context = ContextFor(new InvalidOperationException("something unexpected"));

        new ConflictExceptionFilterAttribute().OnException(context);

        Assert.Null(context.Result);
        Assert.False(context.ExceptionHandled);
    }
}