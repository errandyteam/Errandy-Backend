using System;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Errandy.Application.Interfaces;
using Errandy.Infrastructure.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Errandy.Tests.Payments;

// Isolated tests of this specific service's temporary guards. No database,
// API startup, real escrow, dispute, or payout is involved.
public class EscrowOperationSafetyGuardTests
{
    [Theory]
    [InlineData("lock")]
    [InlineData("adjust")]
    [InlineData("release")]
    [InlineData("refund")]
    [InlineData("partial")]
    public async Task FinancialOperation_IsBlockedBeforeDatabaseAccess(string operation)
    {
        var db = DispatchProxy.Create<IApplicationDbContext, EscrowGuardDbProxy>();
        var proxy = (EscrowGuardDbProxy)db;
        var service = new EscrowService(db, new FixedClock(),
            new ConfigurationBuilder().Build(), NullLogger<EscrowService>.Instance);
        var errand = Guid.NewGuid();
        var user = Guid.NewGuid();
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => operation switch
        {
            "lock" => service.LockFundsAsync(errand, user, 20m, CancellationToken.None),
            "adjust" => service.AdjustLockedFundsAsync(errand, user, 20m, 25m, CancellationToken.None),
            "release" => service.ReleaseFundsAsync(errand, user, 20m, CancellationToken.None),
            "refund" => service.RefundFundsAsync(errand, user, 20m, CancellationToken.None),
            "partial" => service.PartialRefundAsync(errand, user, 10m, 10m, CancellationToken.None),
            _ => throw new ArgumentOutOfRangeException(nameof(operation))
        });
        Assert.Equal("Escrow financial operations are temporarily disabled pending safety review.", error.Message);
        Assert.Equal(0, proxy.DatabaseCalls);
    }

    private sealed class FixedClock : IDateTimeProvider
    {
        public DateTime UtcNow => new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
    }
}

// Public and non-sealed because DispatchProxy creates a subclass at runtime.
public class EscrowGuardDbProxy : DispatchProxy
{
    public int DatabaseCalls { get; private set; }
    protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
    {
        DatabaseCalls++;
        throw new InvalidOperationException("Database access is forbidden in this guard test.");
    }
}
