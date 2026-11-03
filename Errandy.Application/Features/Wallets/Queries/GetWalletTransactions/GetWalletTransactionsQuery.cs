using Errandy.Application.Common.DTOs;
using Errandy.Application.Common.Interfaces;
using Errandy.Application.Common.Results;
using MediatR;

namespace Errandy.Application.Features.Wallets.Queries.GetWalletTransactions;

public record GetWalletTransactionsQuery(int Page = 1, int PageSize = 20)
    : IRequest<Result<IReadOnlyList<TransactionDto>>>;

public class GetWalletTransactionsQueryHandler
    : IRequestHandler<GetWalletTransactionsQuery, Result<IReadOnlyList<TransactionDto>>>
{
    private readonly IWalletRepository _wallets;
    private readonly ITransactionRepository _transactions;
    private readonly ICurrentUser _currentUser;

    public GetWalletTransactionsQueryHandler(
        IWalletRepository wallets,
        ITransactionRepository transactions,
        ICurrentUser currentUser)
    {
        _wallets = wallets;
        _transactions = transactions;
        _currentUser = currentUser;
    }

    public async Task<Result<IReadOnlyList<TransactionDto>>> Handle(
        GetWalletTransactionsQuery request,
        CancellationToken cancellationToken)
    {
        if (!_currentUser.IsAuthenticated || _currentUser.UserId is null)
            return Result<IReadOnlyList<TransactionDto>>.Failure(
                "User is not authenticated.", ErrorType.Unauthorized);

        var wallet = await _wallets.GetByUserIdAsync(_currentUser.UserId.Value, cancellationToken);
        if (wallet is null)
            return Result<IReadOnlyList<TransactionDto>>.Success(Array.Empty<TransactionDto>());

        var page = request.Page < 1 ? 1 : request.Page;
        var pageSize = request.PageSize is < 1 or > 100 ? 20 : request.PageSize;

        var list = await _transactions.GetByWalletIdAsync(wallet.Id, page, pageSize, cancellationToken);
        var dtos = list.Select(tx => TransactionDto.FromEntity(tx)).ToList();

        return Result<IReadOnlyList<TransactionDto>>.Success(dtos);
    }
}