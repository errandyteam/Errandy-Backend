using Errandy.Domain.Common;
using Errandy.Domain.Enums;

namespace Errandy.Domain.Entities;

/// <summary>
/// Represents a runner/customer bank withdrawal request subject to a mandatory
/// 24-hour security hold (EligibleAt = RequestedAt + 24h) before bank payout.
/// </summary>
public class WithdrawalRequest : BaseEntity
{
    public Guid UserId { get; private set; }
    public Guid WalletId { get; private set; }
    public Guid TransactionId { get; private set; }

    public decimal Amount { get; private set; }
    public string Currency { get; private set; } = "NGN";

    public string BankName { get; private set; } = string.Empty;
    public string? BankCode { get; private set; }
    public string AccountNumber { get; private set; } = string.Empty;
    public string AccountName { get; private set; } = string.Empty;

    public WithdrawalStatus Status { get; private set; } = WithdrawalStatus.PendingHold;
    public string Reference { get; private set; } = string.Empty;

    public DateTime RequestedAt { get; private set; }
    public DateTime EligibleAt { get; private set; }
    public DateTime? ProcessedAt { get; private set; }

    public Guid? ProcessedByAdminId { get; private set; }
    public string? AdminNote { get; private set; }

    private WithdrawalRequest() { }

    public static WithdrawalRequest Create(
        Guid userId,
        Guid walletId,
        Guid transactionId,
        decimal amount,
        string bankName,
        string accountNumber,
        string accountName,
        string reference,
        DateTime requestedAtUtc,
        TimeSpan holdDuration,
        string? bankCode = null)
    {
        if (userId == Guid.Empty)
            throw new ArgumentException("UserId is required.", nameof(userId));

        if (walletId == Guid.Empty)
            throw new ArgumentException("WalletId is required.", nameof(walletId));

        if (transactionId == Guid.Empty)
            throw new ArgumentException("TransactionId is required.", nameof(transactionId));

        if (amount <= 0)
            throw new ArgumentException("Withdrawal amount must be greater than zero.", nameof(amount));

        if (string.IsNullOrWhiteSpace(bankName))
            throw new ArgumentException("BankName is required.", nameof(bankName));

        if (string.IsNullOrWhiteSpace(accountNumber))
            throw new ArgumentException("AccountNumber is required.", nameof(accountNumber));

        if (string.IsNullOrWhiteSpace(accountName))
            throw new ArgumentException("AccountName is required.", nameof(accountName));

        var rounded = decimal.Round(amount, 2, MidpointRounding.ToEven);

        return new WithdrawalRequest
        {
            UserId = userId,
            WalletId = walletId,
            TransactionId = transactionId,
            Amount = rounded,
            Currency = "NGN",
            BankName = bankName.Trim(),
            BankCode = string.IsNullOrWhiteSpace(bankCode) ? null : bankCode.Trim(),
            AccountNumber = accountNumber.Trim(),
            AccountName = accountName.Trim(),
            Status = WithdrawalStatus.PendingHold,
            Reference = reference.Trim(),
            RequestedAt = requestedAtUtc,
            EligibleAt = requestedAtUtc.Add(holdDuration)
        };
    }

    public bool IsMatured(DateTime utcNow) => utcNow >= EligibleAt;

    public void Complete(DateTime utcNow, Guid? adminId = null, string? note = null)
    {
        EnsurePending(nameof(Complete));
        Status = WithdrawalStatus.Completed;
        ProcessedAt = utcNow;
        ProcessedByAdminId = adminId;
        AdminNote = string.IsNullOrWhiteSpace(note) ? null : note.Trim();
        UpdatedAt = utcNow;
    }

    public void CancelByUser(DateTime utcNow)
    {
        EnsurePending(nameof(CancelByUser));
        Status = WithdrawalStatus.Cancelled;
        ProcessedAt = utcNow;
        AdminNote = "Cancelled by user during hold period.";
        UpdatedAt = utcNow;
    }

    public void Reject(Guid adminId, string reason, DateTime utcNow)
    {
        EnsurePending(nameof(Reject));
        Status = WithdrawalStatus.Rejected;
        ProcessedAt = utcNow;
        ProcessedByAdminId = adminId;
        AdminNote = string.IsNullOrWhiteSpace(reason) ? "Rejected by administrator." : reason.Trim();
        UpdatedAt = utcNow;
    }

    private void EnsurePending(string action)
    {
        if (Status != WithdrawalStatus.PendingHold)
            throw new InvalidOperationException(
                $"Cannot perform '{action}' because withdrawal '{Reference}' is already in '{Status}' status.");
    }
}
