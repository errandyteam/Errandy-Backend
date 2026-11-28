using System;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Errandy.Api.Contracts;
using Errandy.Api.Controllers;
using Errandy.Application.Common.DTOs;
using Errandy.Application.Common.Results;
using Errandy.Application.Features.Wallets.Commands.CancelWithdrawal;
using Errandy.Application.Features.Wallets.Commands.ProcessWithdrawal;
using MediatR;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Errandy.Tests.Payments;

// Isolated controller tests with a fake sender: no HTTP server, JWT, database,
// real handlers, transaction, payout, or Program.cs startup.
public class WithdrawalConcurrencyControllerTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ConcurrencyException_MapsToConflictWithoutExceptionDetails(bool admin)
    {
        using var setup = new Setup("concurrency");
        var result = await InvokeAsync(setup.Controller, admin);
        var objectResult = Assert.IsType<ObjectResult>(result);
        Assert.Equal(StatusCodes.Status409Conflict, objectResult.StatusCode);
        var problem = Assert.IsType<ProblemDetails>(objectResult.Value);
        Assert.Equal("Conflict", problem.Title);
        Assert.Equal(StatusCodes.Status409Conflict, problem.Status);
        Assert.DoesNotContain("private database detail", problem.Detail ?? string.Empty);
        Assert.Contains("Refresh", problem.Detail ?? string.Empty);
        AssertCommand(setup.Proxy.LastRequest, admin);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task OtherExceptions_AreNotMisreportedAsConcurrency(bool admin)
    {
        using var setup = new Setup("unexpected");
        await Assert.ThrowsAsync<InvalidOperationException>(() => InvokeAsync(setup.Controller, admin));
        AssertCommand(setup.Proxy.LastRequest, admin);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task FailureResult_StillUsesNormalResultMapping(bool admin)
    {
        using var setup = new Setup("forbidden");
        var result = await InvokeAsync(setup.Controller, admin);
        var objectResult = Assert.IsType<ObjectResult>(result);
        Assert.Equal(StatusCodes.Status403Forbidden, objectResult.StatusCode);
        AssertCommand(setup.Proxy.LastRequest, admin);
    }

    private static Task<IActionResult> InvokeAsync(WalletController controller, bool admin) => admin
        ? controller.ProcessWithdrawal(Guid.NewGuid(), new ProcessWithdrawalRequest(false, "test"), CancellationToken.None)
        : controller.CancelWithdrawal(Guid.NewGuid(), CancellationToken.None);

    private static void AssertCommand(object? request, bool admin)
    {
        if (admin) Assert.IsType<ProcessWithdrawalCommand>(request);
        else Assert.IsType<CancelWithdrawalCommand>(request);
    }

    private sealed class Setup : IDisposable
    {
        private readonly ServiceProvider _services;
        public WalletController Controller { get; }
        public WithdrawalConcurrencySenderProxy Proxy { get; }

        public Setup(string mode)
        {
            var sender = DispatchProxy.Create<ISender, WithdrawalConcurrencySenderProxy>();
            Proxy = (WithdrawalConcurrencySenderProxy)sender;
            Proxy.Mode = mode;
            _services = new ServiceCollection().AddSingleton(sender).BuildServiceProvider();
            Controller = new WalletController
            {
                ControllerContext = new ControllerContext
                {
                    HttpContext = new DefaultHttpContext { RequestServices = _services }
                }
            };
        }

        public void Dispose() => _services.Dispose();
    }
}

// Public and non-sealed so DispatchProxy can implement ISender at runtime.
public class WithdrawalConcurrencySenderProxy : DispatchProxy
{
    public string Mode { get; set; } = string.Empty;
    public object? LastRequest { get; private set; }

    protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
    {
        if (targetMethod?.Name != "Send" || args is not { Length: > 0 } ||
            args[0] is not (CancelWithdrawalCommand or ProcessWithdrawalCommand))
            throw new InvalidOperationException("Unexpected sender call in test.");
        LastRequest = args[0];
        return Mode switch
        {
            "concurrency" => Task.FromException<Result<WithdrawalRequestDto>>(
                new DbUpdateConcurrencyException("private database detail")),
            "unexpected" => Task.FromException<Result<WithdrawalRequestDto>>(
                new InvalidOperationException("synthetic unexpected error")),
            "forbidden" => Task.FromResult(Result<WithdrawalRequestDto>.Failure("Forbidden", ErrorType.Forbidden)),
            _ => throw new InvalidOperationException("Unexpected test mode.")
        };
    }
}
