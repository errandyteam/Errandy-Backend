using Errandy.Application.Common.DTOs;
using Errandy.Application.Common.Interfaces;
using Errandy.Application.Common.Results;
using Errandy.Domain.Entities;
using Errandy.Domain.Enums;
using MediatR;
namespace Errandy.Application.Features.Auth.Commands.RegisterUser;

public class RegisterUserCommandHandler
    : IRequestHandler<RegisterUserCommand, Result<AuthResponseDto>>
{
    private readonly IUserRepository _users;
    private readonly IUnitOfWork _uow;
    private readonly IPasswordHasher _hasher;
    private readonly IJwtTokenGenerator _jwt;
    public RegisterUserCommandHandler(
        IUserRepository users,
        IUnitOfWork uow,
        IPasswordHasher hasher,
        IJwtTokenGenerator jwt)
    {
        _users = users;
        _uow = uow;
        _hasher = hasher;
        _jwt = jwt;
    }
    public async Task<Result<AuthResponseDto>> Handle(
        RegisterUserCommand request,
        CancellationToken cancellationToken)
    {
        var email = request.Email.Trim().ToLowerInvariant();
        if (await _users.ExistsByEmailAsync(email, cancellationToken))
            return Result<AuthResponseDto>.Failure(
                "An account with this email already exists.", ErrorType.Conflict);
        var user = new User
        {
            FullName = request.FullName.Trim(),
            Email = email,
            PhoneNumber = request.PhoneNumber.Trim(),
            PasswordHash = _hasher.Hash(request.Password),
            Role = UserRole.Customer // everyone starts as a customer
        };
        await _users.AddAsync(user, cancellationToken);
        await _uow.SaveChangesAsync(cancellationToken);
        var (token, expires) = _jwt.GenerateToken(user);
        return Result<AuthResponseDto>.Success(
            new AuthResponseDto(token, expires, UserDto.FromEntity(user)));
    }
}
