using Errandy.Domain.Enums;
namespace Errandy.Application.Common.Interfaces;
/// <summary>
/// Exposes the identity of the caller resolved from the current HTTP context.
/// </summary>
public interface ICurrentUser
{
    Guid? UserId { get; }
    string? Email { get; }
    UserRole? Role { get; }
    bool IsAuthenticated { get; }
}
