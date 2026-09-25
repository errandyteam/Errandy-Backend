using Errandy.Domain.Common;
using Errandy.Domain.Enums;
namespace Errandy.Domain.Entities;

public class User : BaseEntity
{
    public string FullName { get; set; } = string.Empty;

    public string Email { get; set; } = string.Empty;
    public string PhoneNumber { get; set; } = string.Empty;
    
    public string PasswordHash { get; set; } = string.Empty;
    public UserRole Role { get; set; } = UserRole.Customer;
    
    public RunnerProfile? RunnerProfile { get; set; }
}
