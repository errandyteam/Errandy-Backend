using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Errandy.Application.Features.Escrow;
using Errandy.Application.Interfaces;
using Errandy.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using TransactionEntity = Errandy.Domain.Entities.Transaction;
using WalletTransactionStatus = Errandy.Domain.Enums.TransactionStatus;

namespace Errandy.Infrastructure.Services;

/// <summary>
/// Unwired preparation for a full escrow refund. No commit, external payment,
/// authorization, or transaction boundary is provided here. Do not register or
/// call from the application until the orchestration and concurrent writers
/// are reviewed. The caller must discard the context if any operation fails.
/// </summary>
public sealed class EscrowFullRefundPreparation
{
    private readonly IApplicationDbContext _db;
    private readonly IDateTimeProvider _clock;

    public EscrowFullRefundPreparation(IApplicationDbContext db, IDateTimeProvider clock)
    {
        _db = db ?? throw new ArgumentNullException(nameof(db));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
    }

    public async Task PrepareAsync(Guid errandId, Guid customerId, CancellationToken cancellationToken = default)
    {
        var hold = await _db.EscrowHolds.FirstOrDefaultAsync(h => h.ErrandId == errandId, cancellationToken);
        if (hold is null || hold.CustomerId != customerId)
            throw new InvalidOperationException("A matching escrow hold is required.");
        var wallet = await _db.Wallets.FirstOrDefaultAsync(w => w.UserId == customerId, cancellationToken);
        EscrowSettlementPreconditions.EnsureCustomerBacking(hold, wallet, errandId, customerId);

        // A stale tracked hold must not be accepted just because another hold
        // leaves enough funds in the wallet. xmin must still protect the save.
        var active = _db.EscrowHolds.AsNoTracking().Where(h => h.CustomerId == customerId && h.Status == EscrowHoldStatus.Held);
        var targetCurrent = await active.AnyAsync(h => h.Id == hold.Id &&
            h.CurrentLockedAmount == hold.CurrentLockedAmount && h.Currency == "NGN", cancellationToken);
        if (!targetCurrent)
            throw new InvalidOperationException("The escrow hold changed; refresh the operation.");
        if (await active.AnyAsync(h => h.CurrentLockedAmount < 0m || h.Currency != "NGN", cancellationToken))
            throw new InvalidOperationException("The customer's escrow holds require reconciliation.");
        var totalActiveHolds = await active.SumAsync(h => (decimal?)h.CurrentLockedAmount, cancellationToken) ?? 0m;
        if (wallet!.LockedBalance < totalActiveHolds)
            throw new InvalidOperationException("The customer wallet does not back all active escrow holds.");

        var plan = EscrowSettlementPlan.FullRefund(hold.CurrentLockedAmount);
        var before = wallet.Balance;
        var now = _clock.UtcNow;
        hold.MarkRefunded(plan.CustomerRefund, now);
        wallet.RefundLockedFunds(plan.CustomerRefund);
        await _db.Transactions.AddAsync(new TransactionEntity
        {
            WalletId = wallet.Id,
            ErrandId = errandId,
            Type = TransactionType.Credit,
            Category = TransactionCategory.EscrowRefund,
            Amount = plan.CustomerRefund,
            BalanceBefore = before,
            BalanceAfter = wallet.Balance,
            Status = WalletTransactionStatus.Completed,
            Reference = TransactionEntity.GenerateReference("ESC-RFD"),
            Description = $"Full escrow refund for errand {errandId}"
        }, cancellationToken);
        // Intentionally NO SaveChanges: the orchestration must commit once.
    }
}
