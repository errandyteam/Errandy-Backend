using Errandy.Application.Common.DTOs;
using Errandy.Application.Features.Escrow.Queries.GetErrandTransactions;
using Errandy.Application.Features.Escrow.Queries.GetEscrowByErrandId;
using Errandy.Application.Features.Escrow.Queries.GetMyEscrowHolds;
using Errandy.Application.Features.Escrow.Queries.GetPlatformFinancialSummary;
using Errandy.Domain.Enums;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Errandy.Api.Controllers;

[Authorize]
[Route("api/escrow")]
public class EscrowController : ApiControllerBase
{
    /// <summary>Gets the escrow hold breakdown (locked, released, commission, refunded) for a specific errand.</summary>
    [HttpGet("errand/{errandId:guid}")]
    [ProducesResponseType(typeof(EscrowHoldDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetByErrandId(Guid errandId, CancellationToken ct)
    {
        return HandleResult(await Mediator.Send(new GetEscrowByErrandIdQuery(errandId), ct));
    }

    /// <summary>Gets the ledger audit trail (lock, adjustment, buffer refund, payout, commission) for a specific errand.</summary>
    [HttpGet("errand/{errandId:guid}/transactions")]
    [ProducesResponseType(typeof(IReadOnlyList<TransactionDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetErrandTransactions(Guid errandId, CancellationToken ct)
    {
        return HandleResult(await Mediator.Send(new GetErrandTransactionsQuery(errandId), ct));
    }

    /// <summary>Lists escrow holds belonging to the current authenticated customer or runner.</summary>
    [HttpGet("my-holds")]
    [ProducesResponseType(typeof(IReadOnlyList<EscrowHoldDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> GetMyHolds(
        [FromQuery] EscrowHoldStatus? status = null,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken ct = default)
    {
        return HandleResult(await Mediator.Send(new GetMyEscrowHoldsQuery(status, page, pageSize), ct));
    }

    /// <summary>Admin-only platform financial overview (active escrow, runner payouts, platform commissions, refunds).</summary>
    [HttpGet("admin/summary")]
    [ProducesResponseType(typeof(PlatformFinancialSummaryDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> GetAdminSummary(CancellationToken ct)
    {
        return HandleResult(await Mediator.Send(new GetPlatformFinancialSummaryQuery(), ct));
    }
}