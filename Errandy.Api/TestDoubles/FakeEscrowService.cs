using Errandy.Application.Interfaces;
using Microsoft.Extensions.Logging;

namespace Errandy.Api.TestDoubles;

/// <summary>
/// TEMPORARY — delete this once Engineer B's real EscrowService (in
/// Errandy.Infrastructure) exists and is registered in Program.cs.
/// This lets Engineer C test the full Create/Accept/Complete flow locally
/// without waiting on the Wallet/Escrow implementation. It just logs what
/// would have happened instead of touching any real balance.
/// </summary>
public class FakeEscrowService : IEscrowService
{
    private readonly ILogger<FakeEscrowService> _logger;

    public FakeEscrowService(ILogger<FakeEscrowService> logger)
    {
        _logger = logger;
    }

    public Task LockFundsAsync(Guid errandId, Guid customerId, decimal amount, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation(
            "[FAKE ESCROW] Would lock {Amount} for customer {CustomerId} on errand {ErrandId}",
            amount, customerId, errandId);
        return Task.CompletedTask;
    }

    public Task ReleaseFundsAsync(Guid errandId, Guid runnerId, decimal finalAmount, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation(
            "[FAKE ESCROW] Would release {Amount} to runner {RunnerId} on errand {ErrandId}",
            finalAmount, runnerId, errandId);
        return Task.CompletedTask;
    }

    public Task RefundFundsAsync(Guid errandId, Guid customerId, decimal amount, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation(
            "[FAKE ESCROW] Would refund {Amount} to customer {CustomerId} on errand {ErrandId}",
            amount, customerId, errandId);
        return Task.CompletedTask;
    }

    public Task PartialRefundAsync(Guid errandId, Guid customerId, decimal refundAmount, decimal releaseToRunnerAmount, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation(
            "[FAKE ESCROW] Would partially refund {RefundAmount} to customer {CustomerId} and release {ReleaseAmount} to runner, errand {ErrandId}",
            refundAmount, customerId, releaseToRunnerAmount, errandId);
        return Task.CompletedTask;
    }

    public Task AdjustLockedFundsAsync(Guid errandId, Guid customerId, decimal oldAmount, decimal newAmount, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation(
            "[FAKE ESCROW] Would adjust locked funds for errand {ErrandId} from {OldAmount} to {NewAmount}",
            errandId, oldAmount, newAmount);
        return Task.CompletedTask;
    }
}
