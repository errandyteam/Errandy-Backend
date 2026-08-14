namespace Errandy.Application.Interfaces;

/// <summary>
/// Contract Engineer C depends on to know "who is making this request" without
/// touching Engineer A's Auth/JWT implementation directly. A implements this by
/// reading claims off HttpContext.User (typically in Errandy.Infrastructure or
/// Errandy.Api), registered as:
///   services.AddScoped&lt;ICurrentUserService, CurrentUserService&gt;();
/// </summary>
public interface ICurrentUserService
{
    Guid? UserId { get; }
    string? Role { get; }
    bool IsAuthenticated { get; }
}
