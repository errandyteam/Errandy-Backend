using Errandy.Domain.Entities;
namespace Errandy.Application.Common.DTOs;

public record UserDto(
    Guid Id,
    string FullName,
    string Email,
    string PhoneNumber,
    string Role,
    DateTime CreatedAt)
{
    public static UserDto FromEntity(User user) => new(
        user.Id,
        user.FullName,
        user.Email,
        user.PhoneNumber,
        user.Role.ToString(),
        user.CreatedAt);
}