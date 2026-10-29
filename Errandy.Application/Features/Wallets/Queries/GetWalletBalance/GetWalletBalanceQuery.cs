using Errandy.Application.Common.DTOs;
using Errandy.Application.Common.Interfaces;
using Errandy.Application.Common.Results;
using Errandy.Domain.Entities;
using MediatR;

namespace Errandy.Application.Features.Wallets.Queries.GetWalletBalance;

public record GetWalletBalanceQuery : IRequest<Result<WalletDto>>;

public class GetWalletBalanceQueryHandler
    : IRequestHandler<GetWalletBalanceQuery, Result<WalletDto>>
{
    private readonly IWalletRepository _wallets;
    private readonly ICurrentUser _currentUser;
    private readonly IUnitOfWork _uow;

    public GetWalletBalanceQueryHandler(
        IWalletRepository wallets,
        ICurrentUser currentUser,
        IUnitOfWork uow)
    {
        _wallets = wallets;
        _currentUser = currentUser;
        _uow = uow;
    }

    public async Task<Result<WalletDto>> Handle(
        GetWalletBalanceQuery request,
        CancellationToken cancellationToken)
    {
        if (!_currentUser.IsAuthenticated || _currentUser.UserId is null)
            return Result<WalletDto>.Failure("User is not authenticated.", ErrorType.Unauthorized);

        var userId = _currentUser.UserId.Value;
        var wallet = await _wallets.GetByUserIdAsync(userId, cancellationToken);

        // Defensive fallback: if a pre-existing user (e.g. seeded before Wallet existed) has no wallet, create one.
        if (wallet is null)
        {
            wallet = new Wallet
            {
                UserId = userId,
                Balance = 0m,
                LockedBalance = 0m,
                Currency = "NGN",
                IsActive = true
            };
            await _wallets.AddAsync(wallet, cancellationToken);
            await _uow.SaveChangesAsync(cancellationToken);
        }

        return Result<WalletDto>.Success(WalletDto.FromEntity(wallet));
    }
}
