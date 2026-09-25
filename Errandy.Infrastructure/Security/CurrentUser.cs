using System.Security.Claims;
using Errandy.Application.Common.Interfaces;
using Errandy.Domain.Enums;


using Microsoft.AspNetCore.Http;
namespace Errandy.Infrastructure.Security;
/// <summary>
/// Resolves the calling user's identity from the JWT claims on the current
/// HTTP request.
/// </summary>
public class CurrentUser : ICurrentUser
{
    private readonly IHttpContextAccessor _accessor;
    public CurrentUser(IHttpContextAccessor accessor) => _accessor = accessor;
    private ClaimsPrincipal? Principal => _accessor.HttpContext?.User;
    public bool IsAuthenticated => Principal?.Identity?.IsAuthenticated ?? false;
    public Guid? UserId
    {
        get
        {
            var value = Principal?.FindFirstValue(ClaimTypes.NameIdentifier);
            return Guid.TryParse(value, out var id) ? id : null;
        }
    }
    public string? Email => Principal?.FindFirstValue(ClaimTypes.Email);
    public UserRole? Role
    {
        get
        {
            var value = Principal?.FindFirstValue(ClaimTypes.Role);
            return Enum.TryParse<UserRole>(value, out var role) ? role : null;
        }
    }
}
