using System.Threading;
using System.Threading.Tasks;
using Errandy.Application.Common.Results;
using Errandy.Application.Features.Wallets.Commands.FundWallet;
using Xunit;

namespace Errandy.Tests.Payments;

public class DirectFundingDisabledTests
{
    [Fact]
    public async Task DirectFundCommand_IsForbidden_WithoutTouchingRepositories()
    {
        // The disabled handler must not need or call any of these dependencies.
        // An attempt to use them would fail this test rather than reach a database.
        var handler = new FundWalletCommandHandler(null!, null!, null!, null!);
        var command = new FundWalletCommand(1000m, "TEST-UNVERIFIED", "Test only");

        var result = await handler.Handle(command, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(ErrorType.Forbidden, result.ErrorType);
        Assert.Null(result.Value);
    }
}
