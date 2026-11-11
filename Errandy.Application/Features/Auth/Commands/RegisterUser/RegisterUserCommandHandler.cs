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
    private readonly IRefreshTokenRepository _refreshTokens;
    private readonly IUnitOfWork _uow;
    private readonly IPasswordHasher _hasher;
    private readonly IJwtTokenGenerator _jwt;

    public RegisterUserCommandHandler(
        IUserRepository users,
        IRefreshTokenRepository refreshTokens,
        IUnitOfWork uow,
        IPasswordHasher hasher,
        IJwtTokenGenerator jwt)
    {
        _users = users;
        _refreshTokens = refreshTokens;
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
            Role = UserRole.Customer
        };

        await _users.AddAsync(user, cancellationToken);
        await _uow.SaveChangesAsync(cancellationToken);

        var pair = _jwt.GenerateTokenPair(user);

        var refreshToken = new RefreshToken
        {
            UserId = user.Id,
            TokenHash = _jwt.HashRefreshToken(pair.RefreshToken),
            ExpiresAtUtc = pair.RefreshTokenExpiresAtUtc,
        };
        await _refreshTokens.AddAsync(refreshToken, cancellationToken);
        await _uow.SaveChangesAsync(cancellationToken);

        return Result<AuthResponseDto>.Success(
            new AuthResponseDto(
                pair.AccessToken,
                pair.AccessTokenExpiresAtUtc,
                pair.RefreshToken,
                pair.RefreshTokenExpiresAtUtc,
                UserDto.FromEntity(user)));
    }
}