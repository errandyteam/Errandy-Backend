using System;
using System.Linq;
using Errandy.Domain.Entities;
using Errandy.Domain.Enums;

namespace Errandy.Application.Features.Wallets.Queries;

/// <summary>
/// Builds a read-only filter for disputed errands involving a wallet owner.
/// This is not a lock, and evaluating it does not protect against a dispute
/// opening concurrently or later. It is not wired to withdrawal requests.
/// </summary>
public static class WithdrawalDisputeQuery
{
    public static IQueryable<Errand> ForUser(IQueryable<Errand> errands, Guid userId)
    {
        ArgumentNullException.ThrowIfNull(errands);
        return errands.Where(e =>
            e.Status == ErrandStatus.Disputed &&
            (e.CustomerId == userId || e.RunnerId == userId));
    }
}
