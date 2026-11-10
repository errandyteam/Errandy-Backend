using Errandy.Application.Common.DTOs;
using Errandy.Domain.Entities;

namespace Errandy.Application.Common.Interfaces;

public interface IJwtTokenGenerator
{
    /// <summary>Generates a signed JWT access token and its expiry.</summary>
    (string token, DateTime expiresAtUtc) GenerateAccessToken(User user);

    /// <summary>Generates a secure random refresh token (plaintext) and its expiry.</summary>
    (string token, DateTime expiresAtUtc) GenerateRefreshToken();

    /// <summary>Convenience: access + refresh pair for register/login responses.</summary>
    TokenPair GenerateTokenPair(User user);

    /// <summary>Hashes a refresh token for storage. Uses SHA-256.</summary>
    string HashRefreshToken(string refreshToken);
}