using Errandy.Domain.Common;

namespace Errandy.Domain.Entities;

/// <summary>
/// A refresh token tied to a user. Stored as a hash; plaintext is returned
/// to the client exactly once (at issue time) and never persisted.
/// </summary>
public class RefreshToken : BaseEntity
{
    public Guid UserId { get; set; }
    public User User { get; set; } = null!;

    /// <summary>Hash (SHA-256) of the refresh token value sent to the client.</summary>
    public string TokenHash { get; set; } = string.Empty;

    public DateTime ExpiresAtUtc { get; set; }
    public DateTime? RevokedAtUtc { get; set; }
    public string? ReasonRevoked { get; set; }

    /// <summary>Points to the token that replaced this one (enables chain tracking on reuse).</summary>
    public Guid? ReplacedById { get; set; }
    public RefreshToken? ReplacedBy { get; set; }

    public bool IsActive => RevokedAtUtc is null && DateTime.UtcNow < ExpiresAtUtc;
}