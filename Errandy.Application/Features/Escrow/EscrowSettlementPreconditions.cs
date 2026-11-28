using System;
using Errandy.Domain.Entities;
using Errandy.Domain.Enums;

namespace Errandy.Application.Features.Escrow;

/// <summary>
/// Validates already-loaded settlement inputs without moving money. This does
/// not establish that entities are persisted, serialize concurrent operations,
/// open a transaction, or prove aggregate wallet balances back all other holds.
/// It is not wired to the disabled escrow service.
/// </summary>
public static class EscrowSettlementPreconditions
{
    public static void EnsureCustomerBacking(EscrowHold? hold, Wallet? wallet, Guid errandId, Guid customerId)
    {
        if (errandId == Guid.Empty || customerId == Guid.Empty)
            throw new InvalidOperationException("Errand and customer are required.");
        if (hold is null || hold.ErrandId != errandId || hold.CustomerId != customerId)
            throw new InvalidOperationException("A matching escrow hold is required.");
        if (hold.Status != EscrowHoldStatus.Held || hold.CurrentLockedAmount <= 0m)
            throw new InvalidOperationException("A positive unsettled escrow hold is required.");
        if (wallet is null || wallet.Id == Guid.Empty || wallet.UserId != customerId || !wallet.IsActive)
            throw new InvalidOperationException("An existing active customer wallet is required.");
        if (!IsNgn(hold.Currency) || !IsNgn(wallet.Currency))
            throw new InvalidOperationException("The hold and customer wallet must use NGN.");
        if (wallet.LockedBalance < hold.CurrentLockedAmount)
            throw new InvalidOperationException("The customer wallet does not contain enough locked funds for this hold.");
    }

    public static void EnsureCommissionDestination(decimal commission, Guid? treasuryUserId, Wallet? treasuryWallet)
    {
        if (commission < 0m || commission != decimal.Round(commission, 2, MidpointRounding.ToEven))
            throw new ArgumentOutOfRangeException(nameof(commission), "Commission must be a non-negative amount in whole kobo.");
        if (commission == 0m)
            return;
        if (!treasuryUserId.HasValue || treasuryUserId.Value == Guid.Empty)
            throw new InvalidOperationException("An explicit treasury user is required for commission.");
        if (treasuryWallet is null || treasuryWallet.Id == Guid.Empty ||
            treasuryWallet.UserId != treasuryUserId.Value || !treasuryWallet.IsActive || !IsNgn(treasuryWallet.Currency))
            throw new InvalidOperationException("The configured treasury must have an existing active NGN wallet.");
    }

    private static bool IsNgn(string? currency) => string.Equals(currency, "NGN", StringComparison.OrdinalIgnoreCase);
}
