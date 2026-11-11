using Errandy.Application.Common.Interfaces;
using Errandy.Application.Common.Results;
using MediatR;

namespace Errandy.Application.Features.Auth.Commands.Logout;

public class LogoutCommandHandler : IRequestHandler<LogoutCommand, Result>
{
    private readonly IRefreshTokenRepository _repo;
    private readonly IJwtTokenGenerator _jwt;
    private readonly IUnitOfWork _uow;

    public LogoutCommandHandler(
        IRefreshTokenRepository repo,
        IJwtTokenGenerator jwt,
        IUnitOfWork uow)
    {
        _repo = repo;
        _jwt = jwt;
        _uow = uow;
    }

    public async Task<Result> Handle(LogoutCommand request, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.RefreshToken))
        if (string.IsNullOrWhiteSpace(request.RefreshToken))
            return Result.Success();

        var hash = _jwt.HashRefreshToken(request.RefreshToken);
        var rt = await _repo.GetByHashAsync(hash, ct);

        if (rt is not null && rt.RevokedAtUtc is null)
        {
            rt.RevokedAtUtc = DateTime.UtcNow;
            rt.ReasonRevoked = "Logout";
            _repo.Update(rt);
            await _uow.SaveChangesAsync(ct);
        }

        return Result.Success();
    }
}