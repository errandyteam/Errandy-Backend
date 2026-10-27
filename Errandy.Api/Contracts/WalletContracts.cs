namespace Errandy.Api.Contracts;

public record FundWalletRequest(
    decimal Amount,
    string? Reference = null,
    string? Description = null);
