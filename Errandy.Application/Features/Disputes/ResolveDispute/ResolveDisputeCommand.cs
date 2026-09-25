using Errandy.Domain.Enums;
using MediatR;

namespace Errandy.Application.Features.Disputes.ResolveDispute;

/// <summary>
/// Admin resolves an open dispute (PRD 12.3 — full payment / partial refund /
/// penalty). SLA: 12-24 hours. Amount fields are only required for the
/// resolution types that need them (see validator) — e.g. FullPaymentToRunner
/// needs no amount since it just releases the errand's FinalCost.
/// </summary>
public record ResolveDisputeCommand : IRequest
{
    public Guid DisputeId { get; init; }
    public Guid AdminId { get; init; }
    public DisputeResolutionType Resolution { get; init; }

    /// <summary>Required for PartialRefundToCustomer / PenaltyToRunner — amount returned to customer.</summary>
    public decimal? RefundToCustomerAmount { get; init; }

    /// <summary>Required for PartialRefundToCustomer / PenaltyToRunner — amount released to runner.</summary>
    public decimal? ReleaseToRunnerAmount { get; init; }

    public string? Notes { get; init; }
}
