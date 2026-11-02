using Errandy.Application.Common.DTOs;
using Errandy.Application.Common.Interfaces;
using Errandy.Application.Common.Results;
using Errandy.Application.Interfaces;
using Errandy.Domain.Enums;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace Errandy.Application.Features.Wallets.Commands.AdminSetWalletStatus;

public record AdminSetWalletStatusCommand(
    Guid TargetUserId,
    bool IsActive) : IRequest<Result<WalletDto>>;

public class AdminSetWalletStatusCommandHandler
    : IRequestHandler<AdminSetWalletStatusCommand, Result<WalletDto>>
{
    private readonly IApplicationDbContext _db;
    private readonly ICurrentUser _currentUser;

    public AdminSetWalletStatusCommandHandler(
        IApplicationDbContext db,
        ICurrentUser currentUser)
    {
        _db = db;
        _currentUser = currentUser;
    }

    public async Task<Result<WalletDto>> Handle(
        AdminSetWalletStatusCommand request,
        CancellationToken cancellationToken)
    {
        if (!_currentUser.IsAuthenticated || _currentUser.UserId is null)
            return Result<WalletDto>.Failure("User is not authenticated.", ErrorType.Unauthorized);

        var adminId = _currentUser.UserId.Value;

        var adminRole = await _db.Users
            .AsNoTracking()
            .Where(u => u.Id == adminId)
            .Select(u => (UserRole?)u.Role)
            .FirstOrDefaultAsync(cancellationToken);

        if (adminRole != UserRole.Admin)
            return Result<WalletDto>.Failure(
                "Only administrators can freeze or unfreeze wallets.", ErrorType.Forbidden);

        var wallet = await _db.Wallets
            .FirstOrDefaultAsync(w => w.UserId == request.TargetUserId, cancellationToken);

        if (wallet is null)
            return Result<WalletDto>.Failure(
                $"Wallet for user '{request.TargetUserId}' was not found.", ErrorType.NotFound);

        wallet.SetActiveStatus(request.IsActive);
        await _db.SaveChangesAsync(cancellationToken);

        return Result<WalletDto>.Success(WalletDto.FromEntity(wallet));
    }
}
