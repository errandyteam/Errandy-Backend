using Errandy.Application.Common.Interfaces;
namespace Errandy.Infrastructure.Security;
/// <summary>
/// BCrypt-based password hasher. Uses a work factor of 12 which is a sensible
/// balance between security and performance for production workloads.
/// </summary>
public class BCryptPasswordHasher : IPasswordHasher
{
    private const int WorkFactor = 12;
    public string Hash(string password) =>
        BCrypt.Net.BCrypt.HashPassword(password, WorkFactor);
    public bool Verify(string password, string passwordHash)
    {
        try
        {
            return BCrypt.Net.BCrypt.Verify(password, passwordHash);
        }
        catch (BCrypt.Net.SaltParseException)
        {
            return false;
        }
    }
}