using Errandy.Domain.Common;
using Errandy.Domain.Enums;
using WalletTransactionStatus = Errandy.Domain.Enums.TransactionStatus;

namespace Errandy.Domain.Entities;

public class Transaction : BaseEntity
{
    public Guid WalletId { get; set; }
    public Wallet? Wallet { get; set; }

    /// <summary>
    /// Optional link to an Errand for escrow lock, release, refund, or dispute transactions.
    /// </summary>
    public Guid? ErrandId { get; set; }

    public TransactionType Type { get; set; }
    public TransactionCategory Category { get; set; } = TransactionCategory.Funding;
    public decimal Amount { get; set; }

    /// <summary>
    /// Audit snapshot of the wallet's available balance before this transaction.
    /// </summary>
    public decimal BalanceBefore { get; set; }

    /// <summary>
    /// Audit snapshot of the wallet's available balance after this transaction.
    /// </summary>
    public decimal BalanceAfter { get; set; }

    public WalletTransactionStatus Status { get; set; } = WalletTransactionStatus.Completed;

    /// <summary>
    /// Unique idempotency / audit reference (e.g. TXN-20261024-XXXX or gateway reference).
    /// </summary>
    public string Reference { get; set; } = string.Empty;

    public string Description { get; set; } = string.Empty;

    public static string GenerateReference(string prefix = "TXN")
        => $"{prefix}-{DateTime.UtcNow:yyyyMMddHHmmss}-{Guid.NewGuid().ToString("N")[..8].ToUpperInvariant()}";
}