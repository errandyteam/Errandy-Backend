using Errandy.Application.Common.DTOs;
using Errandy.Application.Common.Interfaces;
using Errandy.Application.Common.Results;
using MediatR;
namespace Errandy.Application.Features.Auth.Commands.LoginUser;

public class LoginUserCommandHandler
    : IRequestHandler<LoginUserCommand, Result<AuthResponseDto>>
{
    private readonly IUserRepository _users;
    private readonly IPasswordHasher _hasher;
    private readonly IJwtTokenGenerator _jwt;
    public LoginUserCommandHandler(
        IUserRepository users,
        IPasswordHasher hasher,
        IJwtTokenGenerator jwt)
    {
        _users = users;
        _hasher = hasher;
        _jwt = jwt;
    }
    public async Task<Result<AuthResponseDto>> Handle(
        LoginUserCommand request,
        CancellationToken cancellationToken)
    {
        var email = request.Email.Trim().ToLowerInvariant();
        var user = await _users.GetByEmailAsync(email, cancellationToken);
        // Uniform error message to avoid user enumeration.
        if (user is null || !_hasher.Verify(request.Password, user.PasswordHash))
            return Result<AuthResponseDto>.Failure(
                "Invalid email or password.", ErrorType.Unauthorized);
        var (token, expires) = _jwt.GenerateToken(user);
        return Result<AuthResponseDto>.Success(
            new AuthResponseDto(token, expires, UserDto.FromEntity(user)));
    }
}
