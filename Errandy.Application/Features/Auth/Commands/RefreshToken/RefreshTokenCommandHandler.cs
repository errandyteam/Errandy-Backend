using Errandy.Application.Common.DTOs;
using Errandy.Application.Common.Interfaces;
using Errandy.Application.Common.Results;
using Errandy.Domain.Entities;
using MediatR;

namespace Errandy.Application.Features.Auth.Commands.RefreshTokens;

public class RefreshTokenCommandHandler
    : IRequestHandler<RefreshTokenCommand, Result<AuthResponseDto>>
{
    private readonly IRefreshTokenRepository _repo;
    private readonly IUserRepository _users;
    private readonly IJwtTokenGenerator _jwt;
    private readonly IUnitOfWork _uow;

    public RefreshTokenCommandHandler(
        IRefreshTokenRepository repo,
        IUserRepository users,
        IJwtTokenGenerator jwt,
        IUnitOfWork uow)
    {
        _repo = repo;
        _users = users;
        _jwt = jwt;
        _uow = uow;
    }

    public async Task<Result<AuthResponseDto>> Handle(
        RefreshTokenCommand request, CancellationToken ct)
    {
        var hash = _jwt.HashRefreshToken(request.RefreshToken);
        var rt = await _repo.GetByHashAsync(hash, ct);

        if (rt is null || !rt.IsActive)
            return Result<AuthResponseDto>.Failure(
                "Invalid or expired refresh token.", ErrorType.Unauthorized);

        // Ensure the user is still active
        var user = await _users.GetByIdAsync(rt.UserId, ct);
        if (user is null || !user.IsActive)
            return Result<AuthResponseDto>.Failure(
                "Invalid or expired refresh token.", ErrorType.Unauthorized);

        // Rotate: revoke the used refresh token and issue a new one
        rt.RevokedAtUtc = DateTime.UtcNow;
        rt.ReasonRevoked = "Rotated";

        var pair = _jwt.GenerateTokenPair(user);

        var newRt = new RefreshToken
        {
            UserId = user.Id,
            TokenHash = _jwt.HashRefreshToken(pair.RefreshToken),
            ExpiresAtUtc = pair.RefreshTokenExpiresAtUtc
        };

        rt.ReplacedBy = newRt;
        _repo.Update(rt);
        await _repo.AddAsync(newRt, ct);
        await _uow.SaveChangesAsync(ct);

        return Result<AuthResponseDto>.Success(
            new AuthResponseDto(
                pair.AccessToken,
                pair.AccessTokenExpiresAtUtc,
                pair.RefreshToken,
                pair.RefreshTokenExpiresAtUtc,
                UserDto.FromEntity(user)));
    }
}