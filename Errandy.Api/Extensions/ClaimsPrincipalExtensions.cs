using System.Security.Claims;
using Errandy.Application.Common.Exceptions;

namespace Errandy.Api.Extensions;

public static class ClaimsPrincipalExtensions
{
    /// <summary>
    /// Returns the logged-in user's ID, read from their JWT. Controllers use
    /// this instead of trusting an ID typed into the request body.
    /// </summary>
    public static Guid GetUserId(this ClaimsPrincipal user)
    {
        var value = user.FindFirstValue(ClaimTypes.NameIdentifier) ?? user.FindFirstValue("sub");

        if (!Guid.TryParse(value, out var userId))
            throw new ForbiddenAccessException("Could not determine the current user from the login token.");

        return userId;
    }
}