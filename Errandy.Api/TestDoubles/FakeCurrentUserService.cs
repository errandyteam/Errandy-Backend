using Errandy.Application.Interfaces;

namespace Errandy.Api.TestDoubles;

/// <summary>
/// TEMPORARY — delete once Engineer A's real CurrentUserService (reading JWT
/// claims off HttpContext.User) exists and is registered in Program.cs.
/// Returns a fixed fake user so endpoints can be exercised locally without
/// a working login flow yet.
/// </summary>
public class FakeCurrentUserService : ICurrentUserService
{
    public Guid? UserId => Guid.Parse("11111111-1111-1111-1111-111111111111");
    public string? Role => "Customer";
    public bool IsAuthenticated => true;
}

/// <summary>
/// Real implementation is trivial enough that Engineer C can ship this one
/// permanently — no need to wait on anyone else for this.
/// </summary>
public class SystemDateTimeProvider : IDateTimeProvider
{
    public DateTime UtcNow => DateTime.UtcNow;
}
