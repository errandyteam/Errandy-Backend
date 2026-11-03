using Errandy.Application.Common.Exceptions;
using Errandy.Application.Interfaces;
using Errandy.Domain.Entities;
using Errandy.Domain.Enums;
using FluentValidation.Results;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using TransactionEntity = Errandy.Domain.Entities.Transaction;
using WalletTransactionStatus = Errandy.Domain.Enums.TransactionStatus;

namespace Errandy.Infrastructure.Services;

/// <summary>
/// Full Escrow & Settlement Engine (Engineer B — Financial System).
/// Week 3 + Week 4:
/// - Tracks per-errand EscrowHold records
/// - Prevents double-locking and double-releasing
/// - Refunds unused customer buffer automatically upon completion
/// - Deducts platform commission and credits the Platform Treasury (Admin) wallet with a Commission ledger record
/// - Settles partial refunds & split payouts for resolved disputes
/// </summary>
public class EscrowService : IEscrowService
{
    private readonly IApplicationDbContext _db;
    private readonly IDateTimeProvider _dateTimeProvider;
    private readonly ILogger<EscrowService> _logger;
    private readonly decimal _commissionRate;
    private readonly Guid? _configuredTreasuryUserId;

    public EscrowService(
        IApplicationDbContext db,
        IDateTimeProvider dateTimeProvider,
        IConfiguration configuration,
        ILogger<EscrowService> logger)
    {
        _db = db;
        _dateTimeProvider = dateTimeProvider;
        _logger = logger;

        var configuredRate = configuration.GetValue<decimal?>("Escrow:CommissionRate");
        _commissionRate = configuredRate is >= 0m and < 1m ? configuredRate.Value : 0.10m;

        var treasuryIdStr = configuration["Escrow:TreasuryUserId"];
        if (Guid.TryParse(treasuryIdStr, out var parsedTreasuryId) && parsedTreasuryId != Guid.Empty)
        {
            _configuredTreasuryUserId = parsedTreasuryId;
        }
    }

    public async Task LockFundsAsync(
        Guid errandId,
        Guid customerId,
        decimal amount,
        CancellationToken cancellationToken = default)
    {
        var roundedAmount = ValidatePositiveAmount(amount, nameof(amount));

        var existingHold = await _db.EscrowHolds
            .FirstOrDefaultAsync(h => h.ErrandId == errandId, cancellationToken);

        if (existingHold is not null)
        {
            throw new ValidationException(new[]
            {
                new ValidationFailure("Escrow", $"Funds are already locked in escrow for errand {errandId}.")
            });
        }

        var wallet = await GetOrCreateWalletAsync(customerId, cancellationToken);
        EnsureWalletActive(wallet);

        if (wallet.Balance < roundedAmount)
        {
            throw new ValidationException(new[]
            {
                new ValidationFailure(
                    "Wallet",
                    $"Insufficient wallet balance to create errand. Available: {wallet.Balance:N2} NGN, Required: {roundedAmount:N2} NGN.")
            });
        }

        var balanceBefore = wallet.Balance;
        wallet.LockFunds(roundedAmount);

        var hold = EscrowHold.Create(errandId, customerId, roundedAmount);
        await _db.EscrowHolds.AddAsync(hold, cancellationToken);

        var lockTx = new TransactionEntity
        {
            WalletId = wallet.Id,
            ErrandId = errandId,
            Type = TransactionType.Debit,
            Category = TransactionCategory.EscrowLock,
            Amount = roundedAmount,
            BalanceBefore = balanceBefore,
            BalanceAfter = wallet.Balance,
            Status = WalletTransactionStatus.Completed,
            Reference = TransactionEntity.GenerateReference("ESC-LCK"),
            Description = $"Escrow lock for errand {errandId}"
        };

        await _db.Transactions.AddAsync(lockTx, cancellationToken);
        await _db.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "[ESCROW] Locked {Amount:N2} NGN from customer {CustomerId} for errand {ErrandId}. Available: {Balance:N2}, Locked: {Locked:N2}",
            roundedAmount, customerId, errandId, wallet.Balance, wallet.LockedBalance);
    }

    public async Task AdjustLockedFundsAsync(
        Guid errandId,
        Guid customerId,
        decimal oldAmount,
        decimal newAmount,
        CancellationToken cancellationToken = default)
    {
        var roundedOld = ValidatePositiveAmount(oldAmount, nameof(oldAmount));
        var roundedNew = ValidatePositiveAmount(newAmount, nameof(newAmount));

        if (roundedNew == roundedOld)
            return;

        var hold = await _db.EscrowHolds
            .FirstOrDefaultAsync(h => h.ErrandId == errandId, cancellationToken);

        if (hold is not null && hold.Status != EscrowHoldStatus.Held)
        {
            throw new ValidationException(new[]
            {
                new ValidationFailure("Escrow", $"Cannot adjust escrow for errand {errandId} because hold status is '{hold.Status}'.")
            });
        }

        var wallet = await GetOrCreateWalletAsync(customerId, cancellationToken);
        EnsureWalletActive(wallet);

        var baselineAmount = hold?.CurrentLockedAmount ?? roundedOld;

        if (roundedNew > baselineAmount)
        {
            var additionalLock = roundedNew - baselineAmount;
            if (wallet.Balance < additionalLock)
            {
                throw new ValidationException(new[]
                {
                    new ValidationFailure(
                        "Wallet",
                        $"Insufficient wallet balance to approve price increase. Additional required: {additionalLock:N2} NGN, Available: {wallet.Balance:N2} NGN.")
                });
            }

            var balanceBefore = wallet.Balance;
            wallet.LockFunds(additionalLock);
            hold?.IncreaseHold(additionalLock);

            var additionalLockTx = new TransactionEntity
            {
                WalletId = wallet.Id,
                ErrandId = errandId,
                Type = TransactionType.Debit,
                Category = TransactionCategory.EscrowLock,
                Amount = additionalLock,
                BalanceBefore = balanceBefore,
                BalanceAfter = wallet.Balance,
                Status = WalletTransactionStatus.Completed,
                Reference = TransactionEntity.GenerateReference("ESC-ADJ"),
                Description = $"Escrow top-up lock for price adjustment on errand {errandId}"
            };

            await _db.Transactions.AddAsync(additionalLockTx, cancellationToken);
        }
        else if (roundedNew < baselineAmount)
        {
            var refundDelta = baselineAmount - roundedNew;
            var actualRefund = Math.Min(refundDelta, wallet.LockedBalance);
            if (actualRefund > 0)
            {
                var balanceBefore = wallet.Balance;
                wallet.RefundLockedFunds(actualRefund);
                hold?.DecreaseHold(actualRefund);

                var refundTx = new TransactionEntity
                {
                    WalletId = wallet.Id,
                    ErrandId = errandId,
                    Type = TransactionType.Credit,
                    Category = TransactionCategory.EscrowRefund,
                    Amount = actualRefund,
                    BalanceBefore = balanceBefore,
                    BalanceAfter = wallet.Balance,
                    Status = WalletTransactionStatus.Completed,
                    Reference = TransactionEntity.GenerateReference("ESC-ADJ"),
                    Description = $"Partial escrow unlock for downward price adjustment on errand {errandId}"
                };

                await _db.Transactions.AddAsync(refundTx, cancellationToken);
            }
        }

        await _db.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "[ESCROW] Adjusted locked funds on errand {ErrandId} for customer {CustomerId} from {Old:N2} to {New:N2}",
            errandId, customerId, baselineAmount, roundedNew);
    }

    public async Task ReleaseFundsAsync(
        Guid errandId,
        Guid runnerId,
        decimal finalAmount,
        CancellationToken cancellationToken = default)
    {
        var roundedFinal = ValidatePositiveAmount(finalAmount, nameof(finalAmount));
        var utcNow = _dateTimeProvider.UtcNow;

        var hold = await _db.EscrowHolds
            .FirstOrDefaultAsync(h => h.ErrandId == errandId, cancellationToken);

        if (hold is not null && hold.Status != EscrowHoldStatus.Held)
        {
            throw new ValidationException(new[]
            {
                new ValidationFailure("Escrow", $"Escrow for errand {errandId} has already been settled ({hold.Status}).")
            });
        }

        Guid? customerId = hold?.CustomerId;
        if (customerId is null)
        {
            customerId = await _db.Errands
                .AsNoTracking()
                .Where(e => e.Id == errandId)
                .Select(e => (Guid?)e.CustomerId)
                .FirstOrDefaultAsync(cancellationToken);
        }

        decimal unusedBufferRefund = 0m;

        if (customerId.HasValue)
        {
            var customerWallet = await GetOrCreateWalletAsync(customerId.Value, cancellationToken);
            var errandLockedAmount = hold?.CurrentLockedAmount ?? Math.Min(roundedFinal, customerWallet.LockedBalance);

            if (hold is not null && roundedFinal > hold.CurrentLockedAmount)
            {
                throw new ValidationException(new[]
                {
                    new ValidationFailure(
                        "FinalCost",
                        $"Final cost ({roundedFinal:N2} NGN) exceeds locked escrow balance ({hold.CurrentLockedAmount:N2} NGN). Approve a price adjustment first.")
                });
            }

            // 1. Consume the final errand cost from the customer's locked balance
            var consumeAmount = Math.Min(roundedFinal, customerWallet.LockedBalance);
            if (consumeAmount > 0)
            {
                customerWallet.ConsumeLockedFunds(consumeAmount);
            }

            // 2. Automatically refund any unused buffer locked for this errand back to the customer's available balance
            if (errandLockedAmount > roundedFinal)
            {
                unusedBufferRefund = decimal.Round(errandLockedAmount - roundedFinal, 2, MidpointRounding.ToEven);
                var refundable = Math.Min(unusedBufferRefund, customerWallet.LockedBalance);
                if (refundable > 0)
                {
                    var customerBalanceBefore = customerWallet.Balance;
                    customerWallet.RefundLockedFunds(refundable);
                    unusedBufferRefund = refundable;

                    var bufferRefundTx = new TransactionEntity
                    {
                        WalletId = customerWallet.Id,
                        ErrandId = errandId,
                        Type = TransactionType.Credit,
                        Category = TransactionCategory.EscrowRefund,
                        Amount = refundable,
                        BalanceBefore = customerBalanceBefore,
                        BalanceAfter = customerWallet.Balance,
                        Status = WalletTransactionStatus.Completed,
                        Reference = TransactionEntity.GenerateReference("ESC-BUF"),
                        Description = $"Unused escrow buffer refund for errand {errandId}"
                    };

                    await _db.Transactions.AddAsync(bufferRefundTx, cancellationToken);
                }
            }
        }

        // 3. Calculate platform commission and credit net payout to the runner
        var runnerWallet = await GetOrCreateWalletAsync(runnerId, cancellationToken);
        EnsureWalletActive(runnerWallet);

        var commission = decimal.Round(roundedFinal * _commissionRate, 2, MidpointRounding.ToEven);
        var netRunnerPayout = roundedFinal - commission;

        var runnerBalanceBefore = runnerWallet.Balance;
        runnerWallet.Credit(netRunnerPayout);

        var releaseTx = new TransactionEntity
        {
            WalletId = runnerWallet.Id,
            ErrandId = errandId,
            Type = TransactionType.Credit,
            Category = TransactionCategory.EscrowRelease,
            Amount = netRunnerPayout,
            BalanceBefore = runnerBalanceBefore,
            BalanceAfter = runnerWallet.Balance,
            Status = WalletTransactionStatus.Completed,
            Reference = TransactionEntity.GenerateReference("ESC-REL"),
            Description = $"Escrow payout for errand {errandId} (Gross: {roundedFinal:N2} NGN, Commission: {commission:N2} NGN)"
        };

        await _db.Transactions.AddAsync(releaseTx, cancellationToken);

        // 4. Week 4: Credit Platform Treasury (Admin) wallet with the commission so funds are strictly conserved
        if (commission > 0)
        {
            await CreditPlatformCommissionAsync(
                errandId,
                commission,
                runnerId,
                "ESC-COM",
                $"Platform commission ({_commissionRate:P0}) on errand {errandId}",
                cancellationToken);
        }

        hold?.MarkReleased(runnerId, netRunnerPayout, commission, unusedBufferRefund, utcNow);

        await _db.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "[ESCROW] Released {NetAmount:N2} NGN (Commission: {Commission:N2} NGN, Buffer Refunded: {Buffer:N2} NGN) on errand {ErrandId} to runner {RunnerId}",
            netRunnerPayout, commission, unusedBufferRefund, errandId, runnerId);
    }

    public async Task RefundFundsAsync(
        Guid errandId,
        Guid customerId,
        decimal amount,
        CancellationToken cancellationToken = default)
    {
        var utcNow = _dateTimeProvider.UtcNow;

        var hold = await _db.EscrowHolds
            .FirstOrDefaultAsync(h => h.ErrandId == errandId, cancellationToken);

        if (hold is not null && hold.Status != EscrowHoldStatus.Held)
        {
            throw new ValidationException(new[]
            {
                new ValidationFailure("Escrow", $"Escrow for errand {errandId} has already been settled ({hold.Status}).")
            });
        }

        // Prefer the exact remaining locked amount on the EscrowHold if present so no buffer is left stranded
        var targetRefund = hold?.CurrentLockedAmount ?? decimal.Round(amount, 2, MidpointRounding.ToEven);
        if (targetRefund <= 0)
            return;

        var customerWallet = await GetOrCreateWalletAsync(customerId, cancellationToken);
        EnsureWalletActive(customerWallet);

        var balanceBefore = customerWallet.Balance;

        if (customerWallet.LockedBalance >= targetRefund)
        {
            customerWallet.RefundLockedFunds(targetRefund);
        }
        else if (customerWallet.LockedBalance > 0)
        {
            var remaining = targetRefund - customerWallet.LockedBalance;
            customerWallet.RefundLockedFunds(customerWallet.LockedBalance);
            if (remaining > 0)
                customerWallet.Credit(remaining);
        }
        else
        {
            customerWallet.Credit(targetRefund);
        }

        var refundTx = new TransactionEntity
        {
            WalletId = customerWallet.Id,
            ErrandId = errandId,
            Type = TransactionType.Credit,
            Category = TransactionCategory.EscrowRefund,
            Amount = targetRefund,
            BalanceBefore = balanceBefore,
            BalanceAfter = customerWallet.Balance,
            Status = WalletTransactionStatus.Completed,
            Reference = TransactionEntity.GenerateReference("ESC-RFD"),
            Description = $"Escrow refund for errand {errandId}"
        };

        await _db.Transactions.AddAsync(refundTx, cancellationToken);
        hold?.MarkRefunded(targetRefund, utcNow);

        await _db.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "[ESCROW] Refunded {Amount:N2} NGN to customer {CustomerId} on errand {ErrandId}",
            targetRefund, customerId, errandId);
    }

    public async Task PartialRefundAsync(
        Guid errandId,
        Guid customerId,
        decimal refundAmount,
        decimal releaseToRunnerAmount,
        CancellationToken cancellationToken = default)
    {
        if (refundAmount < 0 || releaseToRunnerAmount < 0)
        {
            throw new ValidationException(new[]
            {
                new ValidationFailure("Escrow", "Dispute refund and release amounts cannot be negative.")
            });
        }

        var roundedRefund = refundAmount > 0 ? decimal.Round(refundAmount, 2, MidpointRounding.ToEven) : 0m;
        var roundedRelease = releaseToRunnerAmount > 0 ? decimal.Round(releaseToRunnerAmount, 2, MidpointRounding.ToEven) : 0m;

        var utcNow = _dateTimeProvider.UtcNow;

        var hold = await _db.EscrowHolds
            .FirstOrDefaultAsync(h => h.ErrandId == errandId, cancellationToken);

        if (hold is not null)
        {
            if (hold.Status != EscrowHoldStatus.Held)
            {
                throw new ValidationException(new[]
                {
                    new ValidationFailure("Escrow", $"Escrow for errand {errandId} has already been settled ({hold.Status}).")
                });
            }

            if (roundedRefund + roundedRelease > hold.CurrentLockedAmount)
            {
                throw new ValidationException(new[]
                {
                    new ValidationFailure(
                        "Escrow",
                        $"Combined dispute refund ({roundedRefund:N2}) and runner release ({roundedRelease:N2}) exceeds locked escrow ({hold.CurrentLockedAmount:N2} NGN).")
                });
            }

            // Ensure any unallocated buffer from CurrentLockedAmount is also returned to the customer
            var unallocatedRemainder = hold.CurrentLockedAmount - (roundedRefund + roundedRelease);
            if (unallocatedRemainder > 0)
            {
                roundedRefund += unallocatedRemainder;
            }
        }

        if (roundedRefund == 0m && roundedRelease == 0m)
            return;

        // If 100% goes back to customer and 0 to runner, settle cleanly as a full refund
        if (roundedRelease == 0m && roundedRefund > 0m)
        {
            await RefundFundsAsync(errandId, customerId, roundedRefund, cancellationToken);
            return;
        }

        var customerWallet = await GetOrCreateWalletAsync(customerId, cancellationToken);
        EnsureWalletActive(customerWallet);

        // 1. Refund customer portion from LockedBalance -> Balance
        if (roundedRefund > 0)
        {
            var customerBalanceBefore = customerWallet.Balance;
            if (customerWallet.LockedBalance >= roundedRefund)
            {
                customerWallet.RefundLockedFunds(roundedRefund);
            }
            else if (customerWallet.LockedBalance > 0)
            {
                var extra = roundedRefund - customerWallet.LockedBalance;
                customerWallet.RefundLockedFunds(customerWallet.LockedBalance);
                if (extra > 0)
                    customerWallet.Credit(extra);
            }
            else
            {
                customerWallet.Credit(roundedRefund);
            }

            var refundTx = new TransactionEntity
            {
                WalletId = customerWallet.Id,
                ErrandId = errandId,
                Type = TransactionType.Credit,
                Category = TransactionCategory.EscrowRefund,
                Amount = roundedRefund,
                BalanceBefore = customerBalanceBefore,
                BalanceAfter = customerWallet.Balance,
                Status = WalletTransactionStatus.Completed,
                Reference = TransactionEntity.GenerateReference("ESC-DSP"),
                Description = $"Dispute partial refund for errand {errandId}"
            };

            await _db.Transactions.AddAsync(refundTx, cancellationToken);
        }

        // 2. Release runner portion (minus commission) + credit Platform Treasury commission
        Guid? runnerId = hold?.RunnerId;
        decimal netRunnerPayout = 0m;
        decimal commission = 0m;

        if (roundedRelease > 0)
        {
            var consumeFromCustomer = Math.Min(roundedRelease, customerWallet.LockedBalance);
            if (consumeFromCustomer > 0)
            {
                customerWallet.ConsumeLockedFunds(consumeFromCustomer);
            }

            if (runnerId is null)
            {
                runnerId = await _db.Errands
                    .AsNoTracking()
                    .Where(e => e.Id == errandId)
                    .Select(e => e.RunnerId)
                    .FirstOrDefaultAsync(cancellationToken);
            }

            if (!runnerId.HasValue)
            {
                throw new ValidationException(new[]
                {
                    new ValidationFailure("RunnerId", $"Cannot release dispute funds to runner because errand {errandId} has no assigned runner.")
                });
            }

            var runnerWallet = await GetOrCreateWalletAsync(runnerId.Value, cancellationToken);
            EnsureWalletActive(runnerWallet);

            commission = decimal.Round(roundedRelease * _commissionRate, 2, MidpointRounding.ToEven);
            netRunnerPayout = roundedRelease - commission;

            var runnerBalanceBefore = runnerWallet.Balance;
            runnerWallet.Credit(netRunnerPayout);

            var releaseTx = new TransactionEntity
            {
                WalletId = runnerWallet.Id,
                ErrandId = errandId,
                Type = TransactionType.Credit,
                Category = TransactionCategory.EscrowRelease,
                Amount = netRunnerPayout,
                BalanceBefore = runnerBalanceBefore,
                BalanceAfter = runnerWallet.Balance,
                Status = WalletTransactionStatus.Completed,
                Reference = TransactionEntity.GenerateReference("ESC-DSP"),
                Description = $"Dispute partial release for errand {errandId} (Gross: {roundedRelease:N2} NGN, Commission: {commission:N2} NGN)"
            };

            await _db.Transactions.AddAsync(releaseTx, cancellationToken);

            if (commission > 0)
            {
                await CreditPlatformCommissionAsync(
                    errandId,
                    commission,
                    runnerId.Value,
                    "ESC-COM",
                    $"Platform commission ({_commissionRate:P0}) on disputed errand {errandId}",
                    cancellationToken);
            }
        }

        hold?.MarkPartiallyReleased(runnerId, roundedRefund, netRunnerPayout, commission, utcNow);

        await _db.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "[ESCROW] Partial dispute settlement on errand {ErrandId}: Refunded {Refund:N2} NGN to customer {CustomerId}, Released {NetRelease:N2} NGN (Commission: {Commission:N2} NGN) to runner {RunnerId}",
            errandId, roundedRefund, customerId, netRunnerPayout, commission, runnerId);
    }

    private async Task CreditPlatformCommissionAsync(
        Guid errandId,
        decimal commission,
        Guid runnerId,
        string referencePrefix,
        string description,
        CancellationToken ct)
    {
        Guid? treasuryUserId = _configuredTreasuryUserId;

        if (!treasuryUserId.HasValue)
        {
            treasuryUserId = await _db.Users
                .AsNoTracking()
                .Where(u => u.Role == UserRole.Admin && u.Id != runnerId)
                .OrderBy(u => u.Id)
                .Select(u => (Guid?)u.Id)
                .FirstOrDefaultAsync(ct);
        }

        if (!treasuryUserId.HasValue)
        {
            _logger.LogWarning(
                "[ESCROW] No Admin/Treasury user found to credit {Commission:N2} NGN commission for errand {ErrandId}. Commission is still recorded on EscrowHold.",
                commission, errandId);
            return;
        }

        var treasuryWallet = await GetOrCreateWalletAsync(treasuryUserId.Value, ct);
        if (!treasuryWallet.IsActive)
            return;

        var treasuryBalanceBefore = treasuryWallet.Balance;
        treasuryWallet.Credit(commission);

        var commissionTx = new TransactionEntity
        {
            WalletId = treasuryWallet.Id,
            ErrandId = errandId,
            Type = TransactionType.Credit,
            Category = TransactionCategory.Commission,
            Amount = commission,
            BalanceBefore = treasuryBalanceBefore,
            BalanceAfter = treasuryWallet.Balance,
            Status = WalletTransactionStatus.Completed,
            Reference = TransactionEntity.GenerateReference(referencePrefix),
            Description = description
        };

        await _db.Transactions.AddAsync(commissionTx, ct);
    }

    private async Task<Wallet> GetOrCreateWalletAsync(Guid userId, CancellationToken ct)
    {
        var wallet = await _db.Wallets.FirstOrDefaultAsync(w => w.UserId == userId, ct);
        if (wallet is not null)
            return wallet;

        wallet = new Wallet
        {
            UserId = userId,
            Balance = 0m,
            LockedBalance = 0m,
            Currency = "NGN",
            IsActive = true
        };
        await _db.Wallets.AddAsync(wallet, ct);
        return wallet;
    }

    private static decimal ValidatePositiveAmount(decimal amount, string paramName)
    {
        if (amount <= 0)
        {
            throw new ValidationException(new[]
            {
                new ValidationFailure(paramName, "Escrow amount must be greater than zero.")
            });
        }

        return decimal.Round(amount, 2, MidpointRounding.ToEven);
    }

    private static void EnsureWalletActive(Wallet wallet)
    {
        if (!wallet.IsActive)
        {
            throw new ValidationException(new[]
            {
                new ValidationFailure("Wallet", "Wallet is currently inactive or restricted.")
            });
        }
    }
}