using Errandy.Domain.Entities;
namespace Errandy.Application.Common.Interfaces;

public interface IJwtTokenGenerator
{
    /// <summary>Generates a signed JWT and returns the token plus its UTC expiry.</summary>
    (string token, DateTime expiresAtUtc) GenerateToken(User user);
}