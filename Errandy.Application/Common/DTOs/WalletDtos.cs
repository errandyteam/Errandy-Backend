using Errandy.Domain.Entities;
using Errandy.Domain.Enums;
using TransactionEntity = Errandy.Domain.Entities.Transaction;
using WalletTransactionStatus = Errandy.Domain.Enums.TransactionStatus;

namespace Errandy.Application.Common.DTOs;

public record WalletDto(
    Guid Id,
    Guid UserId,
    decimal Balance,
    decimal LockedBalance,
    decimal TotalBalance,
    string Currency,
    bool IsActive,
    DateTime CreatedAt,
    DateTime? UpdatedAt)
{
    public static WalletDto FromEntity(Wallet wallet) => new(
        wallet.Id,
        wallet.UserId,
        wallet.Balance,
        wallet.LockedBalance,
        wallet.TotalBalance,
        wallet.Currency,
        wallet.IsActive,
        wallet.CreatedAt,
        wallet.UpdatedAt);
}

public record TransactionDto(
    Guid Id,
    Guid WalletId,
    Guid? ErrandId,
    TransactionType Type,
    string TypeName,
    TransactionCategory Category,
    string CategoryName,
    decimal Amount,
    decimal BalanceBefore,
    decimal BalanceAfter,
    WalletTransactionStatus Status,
    string StatusName,
    string Reference,
    string Description,
    DateTime CreatedAt)
{
    public static TransactionDto FromEntity(TransactionEntity tx) => new(
        tx.Id,
        tx.WalletId,
        tx.ErrandId,
        tx.Type,
        tx.Type.ToString(),
        tx.Category,
        tx.Category.ToString(),
        tx.Amount,
        tx.BalanceBefore,
        tx.BalanceAfter,
        tx.Status,
        tx.Status.ToString(),
        tx.Reference,
        tx.Description,
        tx.CreatedAt);
}

public record WithdrawalRequestDto(
    Guid Id,
    Guid UserId,
    Guid WalletId,
    Guid TransactionId,
    decimal Amount,
    string Currency,
    string BankName,
    string? BankCode,
    string AccountNumber,
    string AccountName,
    WithdrawalStatus Status,
    string StatusName,
    string Reference,
    DateTime RequestedAt,
    DateTime EligibleAt,
    bool IsHoldMatured,
    DateTime? ProcessedAt,
    Guid? ProcessedByAdminId,
    string? AdminNote)
{
    public static WithdrawalRequestDto FromEntity(WithdrawalRequest w, DateTime utcNow) => new(
        w.Id,
        w.UserId,
        w.WalletId,
        w.TransactionId,
        w.Amount,
        w.Currency,
        w.BankName,
        w.BankCode,
        w.AccountNumber,
        w.AccountName,
        w.Status,
        w.Status.ToString(),
        w.Reference,
        w.RequestedAt,
        w.EligibleAt,
        w.IsMatured(utcNow),
        w.ProcessedAt,
        w.ProcessedByAdminId,
        w.AdminNote);
}
