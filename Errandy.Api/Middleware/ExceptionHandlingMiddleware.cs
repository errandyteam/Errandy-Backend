using System.Text.Json;
using Errandy.Application.Common.Exceptions;
using Microsoft.AspNetCore.Mvc;

namespace Errandy.Api.Middleware;

/// <summary>
/// Converts unhandled exceptions into RFC 7807 ProblemDetails responses.
/// Validation errors return 400, missing resources return 404,
/// forbidden actions return 403, and unexpected errors return 500.
/// </summary>
public class ExceptionHandlingMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<ExceptionHandlingMiddleware> _logger;

    public ExceptionHandlingMiddleware(
        RequestDelegate next,
        ILogger<ExceptionHandlingMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await _next(context);
        }
        catch (ValidationException vex)
        {
            _logger.LogWarning(
                "Validation failed: {Errors}",
                vex.Errors);

            await WriteValidationProblem(context, vex);
        }
        catch (NotFoundException nfex)
        {
            _logger.LogWarning(
                "Resource not found: {Message}",
                nfex.Message);

            await WriteProblem(
                context,
                StatusCodes.Status404NotFound,
                "NotFound",
                nfex.Message);
        }
        catch (ForbiddenAccessException faex)
        {
            _logger.LogWarning(
                "Forbidden access: {Message}",
                faex.Message);

            await WriteProblem(
                context,
                StatusCodes.Status403Forbidden,
                "Forbidden",
                faex.Message);
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Unhandled exception processing {Path}",
                context.Request.Path);

            await WriteProblem(
                context,
                StatusCodes.Status500InternalServerError,
                "ServerError",
                "An unexpected error occurred.");
        }
    }

    private static async Task WriteValidationProblem(
        HttpContext context,
        ValidationException vex)
    {
        var problem = new ValidationProblemDetails(
            vex.Errors.ToDictionary(
                kvp => kvp.Key,
                kvp => kvp.Value))
        {
            Title = "Validation",
            Status = StatusCodes.Status400BadRequest
        };

        context.Response.StatusCode =
            StatusCodes.Status400BadRequest;

        context.Response.ContentType =
            "application/problem+json";

        await context.Response.WriteAsync(
            JsonSerializer.Serialize(problem));
    }

    private static async Task WriteProblem(
        HttpContext context,
        int status,
        string title,
        string detail)
    {
        var problem = new ProblemDetails
        {
            Title = title,
            Detail = detail,
            Status = status
        };

        context.Response.StatusCode = status;
        context.Response.ContentType = "application/problem+json";

        await context.Response.WriteAsync(
            JsonSerializer.Serialize(problem));
    }
}