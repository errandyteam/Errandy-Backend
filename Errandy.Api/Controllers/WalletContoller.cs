using Errandy.Api.Contracts;
using Errandy.Application.Common.DTOs;
using Errandy.Application.Features.Wallets.Commands.AdminAdjustWallet;
using Errandy.Application.Features.Wallets.Commands.AdminSetWalletStatus;
using Errandy.Application.Features.Wallets.Commands.CancelWithdrawal;
using Errandy.Application.Features.Wallets.Commands.FundWallet;
using Errandy.Application.Features.Wallets.Commands.ProcessMaturedWithdrawals;
using Errandy.Application.Features.Wallets.Commands.ProcessWithdrawal;
using Errandy.Application.Features.Wallets.Commands.RequestWithdrawal;
using Errandy.Application.Features.Wallets.Queries.GetMyWithdrawals;
using Errandy.Application.Features.Wallets.Queries.GetPendingWithdrawals;
using Errandy.Application.Features.Wallets.Queries.GetRunnerEarnings;
using Errandy.Application.Features.Wallets.Queries.GetWalletBalance;
using Errandy.Application.Features.Wallets.Queries.GetWalletTransactions;
using Errandy.Domain.Enums;
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

    /// <summary>Gets the current authenticated runner's gross earnings, deducted commissions, and net payouts.</summary>
    [HttpGet("earnings")]
    [ProducesResponseType(typeof(RunnerEarningsSummaryDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> GetEarnings(CancellationToken ct)
    {
        return HandleResult(await Mediator.Send(new GetRunnerEarningsQuery(), ct));
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

    /// <summary>Initiates a bank withdrawal subject to a 24-hour security hold (immediately debits spendable balance).</summary>
    [HttpPost("withdraw")]
    [ProducesResponseType(typeof(WithdrawalRequestDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> Withdraw(
        [FromBody] WithdrawWalletRequest request,
        CancellationToken ct)
    {
        var command = new RequestWithdrawalCommand(
            request.Amount,
            request.BankName,
            request.AccountNumber,
            request.AccountName,
            request.BankCode);

        return HandleResult(await Mediator.Send(command, ct));
    }

    /// <summary>Lists the current authenticated user's bank withdrawal requests and 24h hold status.</summary>
    [HttpGet("withdrawals")]
    [ProducesResponseType(typeof(IReadOnlyList<WithdrawalRequestDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> GetMyWithdrawals(
        [FromQuery] WithdrawalStatus? status = null,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken ct = default)
    {
        return HandleResult(await Mediator.Send(new GetMyWithdrawalsQuery(status, page, pageSize), ct));
    }

    /// <summary>Cancels a pending withdrawal while still in its 24-hour hold and restores funds to wallet balance.</summary>
    [HttpPost("withdrawals/{withdrawalId:guid}/cancel")]
    [ProducesResponseType(typeof(WithdrawalRequestDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> CancelWithdrawal(Guid withdrawalId, CancellationToken ct)
    {
        return HandleResult(await Mediator.Send(new CancelWithdrawalCommand(withdrawalId), ct));
    }

    /// <summary>Admin-only: Lists platform withdrawal requests (filterable by status and 24h maturity).</summary>
    [HttpGet("admin/withdrawals")]
    [ProducesResponseType(typeof(IReadOnlyList<WithdrawalRequestDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> GetAdminWithdrawals(
        [FromQuery] WithdrawalStatus? status = WithdrawalStatus.PendingHold,
        [FromQuery] bool maturedOnly = false,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50,
        CancellationToken ct = default)
    {
        return HandleResult(await Mediator.Send(
            new GetPendingWithdrawalsQuery(status, maturedOnly, page, pageSize), ct));
    }

    /// <summary>Admin-only: Approves (completes) or rejects (refunds) a specific withdrawal request.</summary>
    [HttpPost("admin/withdrawals/{withdrawalId:guid}/process")]
    [ProducesResponseType(typeof(WithdrawalRequestDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> ProcessWithdrawal(
        Guid withdrawalId,
        [FromBody] ProcessWithdrawalRequest request,
        CancellationToken ct)
    {
        var command = new ProcessWithdrawalCommand(
            withdrawalId,
            request.Approve,
            request.Note,
            request.ForceBypassHold);

        return HandleResult(await Mediator.Send(command, ct));
    }

    /// <summary>Admin-only: Batch-settles all pending withdrawals whose 24-hour hold window has elapsed.</summary>
    [HttpPost("admin/withdrawals/process-matured")]
    [ProducesResponseType(typeof(MaturedWithdrawalsBatchResultDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> ProcessMaturedWithdrawals(CancellationToken ct)
    {
        return HandleResult(await Mediator.Send(new ProcessMaturedWithdrawalsCommand(), ct));
    }

    /// <summary>Admin-only: Manually credits or debits/penalizes a user's wallet with a mandatory audit reason.</summary>
    [HttpPost("admin/adjust")]
    [ProducesResponseType(typeof(TransactionDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> AdminAdjustWallet(
        [FromBody] AdminAdjustWalletRequest request,
        CancellationToken ct)
    {
        var command = new AdminAdjustWalletCommand(
            request.TargetUserId,
            request.AdjustmentType,
            request.Amount,
            request.Reason,
            request.IsPenalty);

        return HandleResult(await Mediator.Send(command, ct));
    }

    /// <summary>Admin-only: Freezes or unfreezes a user's wallet.</summary>
    [HttpPatch("admin/{userId:guid}/status")]
    [ProducesResponseType(typeof(WalletDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> AdminSetWalletStatus(
        Guid userId,
        [FromBody] AdminSetWalletStatusRequest request,
        CancellationToken ct)
    {
        return HandleResult(await Mediator.Send(
            new AdminSetWalletStatusCommand(userId, request.IsActive), ct));
    }
}
