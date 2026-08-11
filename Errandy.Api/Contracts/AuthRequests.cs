namespace Errandy.Api.Contracts;

public record RegisterRequest(
    string FullName,
    string Email,
    string PhoneNumber,
    string Password);
public record LoginRequest(string Email, string Password);