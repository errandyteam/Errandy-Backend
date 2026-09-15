namespace Errandy.Application.Common.DTOs;

public record AuthResponseDto(
    string Token,
    DateTime ExpiresAtUtc,
    UserDto User);