namespace Errandy.Application.Interfaces;

/// <summary>
/// Contract owned by Engineer C's needs, implemented by Engineer B's Wallet/Escrow
/// system. C never touches wallet balances directly — it only calls these methods
/// at the right points in the errand lifecycle (create -> lock, confirm -> release,
/// dispute resolution -> release/refund).
///
/// IMPORTANT for whoever implements this in Errandy.Infrastructure (Engineer B):
/// register the concrete class in DI as
///   services.AddScoped&lt;IEscrowService, EscrowService&gt;();
/// Until that exists, Engineer C can unit test against a fake/mock implementation.
/// </summary>
public interface IEscrowService
{
    /// <summary>Called when a Customer creates an Errand. Should lock EstimatedCost (+ buffer, per PRD 6.1) from the customer's wallet.</summary>
    Task LockFundsAsync(Guid errandId, Guid customerId, decimal amount, CancellationToken cancellationToken = default);

    /// <summary>Called when Customer confirms completion or auto-release fires. Releases funds to the runner minus commission (PRD 6.2).</summary>
    Task ReleaseFundsAsync(Guid errandId, Guid runnerId, decimal finalAmount, CancellationToken cancellationToken = default);

    /// <summary>Called alongside ReleaseFundsAsync (or on cancellation) to return the unused buffer / full amount to the customer.</summary>
    Task RefundFundsAsync(Guid errandId, Guid customerId, decimal amount, CancellationToken cancellationToken = default);

    /// <summary>Called by ResolveDisputeCommandHandler when Admin picks a PartialRefund outcome (PRD 12.3).</summary>
    Task PartialRefundAsync(Guid errandId, Guid customerId, decimal refundAmount, decimal releaseToRunnerAmount, CancellationToken cancellationToken = default);
}
