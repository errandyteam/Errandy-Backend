using Errandy.Api.Contracts;
using Errandy.Application.Common.DTOs;
using Errandy.Application.Features.Wallets.Commands.FundWallet;
using Errandy.Application.Features.Wallets.Queries.GetWalletBalance;
using Errandy.Application.Features.Wallets.Queries.GetWalletTransactions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Errandy.Api.Controllers;

[Authorize]
[Route("api/wallet")]
public class WalletController : ApiControllerBase
{
    /// <summary>Gets the current authenticated user's wallet balance (available + locked in escrow).</summary>
    [HttpGet("balance")]
    [ProducesResponseType(typeof(WalletDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> GetBalance(CancellationToken ct)
    {
        return HandleResult(await Mediator.Send(new GetWalletBalanceQuery(), ct));
    }

    /// <summary>Gets the current authenticated user's wallet transaction ledger history.</summary>
    [HttpGet("transactions")]
    [ProducesResponseType(typeof(IReadOnlyList<TransactionDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> GetTransactions(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken ct = default)
    {
        return HandleResult(await Mediator.Send(new GetWalletTransactionsQuery(page, pageSize), ct));
    }

    /// <summary>Funds (credits) the current authenticated user's wallet and logs a ledger transaction.</summary>
    [HttpPost("fund")]
    [ProducesResponseType(typeof(TransactionDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> FundWallet(
        [FromBody] FundWalletRequest request,
        CancellationToken ct)
    {
        var command = new FundWalletCommand(
            request.Amount,
            request.Reference,
            request.Description);

        return HandleResult(await Mediator.Send(command, ct));
    }
}
