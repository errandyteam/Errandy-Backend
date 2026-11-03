using Errandy.Domain.Enums;

namespace Errandy.Api.Contracts;

public record FundWalletRequest(
    decimal Amount,
    string? Reference = null,
    string? Description = null);

public record WithdrawWalletRequest(
    decimal Amount,
    string BankName,
    string AccountNumber,
    string AccountName,
    string? BankCode = null);

public record ProcessWithdrawalRequest(
    bool Approve,
    string? Note = null,
    bool ForceBypassHold = false);

public record AdminAdjustWalletRequest(
    Guid TargetUserId,
    TransactionType AdjustmentType,
    decimal Amount,
    string Reason,
    bool IsPenalty = false);

public record AdminSetWalletStatusRequest(
    bool IsActive);
