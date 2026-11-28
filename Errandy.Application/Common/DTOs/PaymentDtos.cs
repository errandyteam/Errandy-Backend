using Errandy.Domain.Entities;
using Errandy.Domain.Enums;

namespace Errandy.Application.Common.DTOs;

public record PaymentInitializationDto(
    Guid PaymentIntentId,
    string Reference,
    string AuthorizationUrl,
    string AccessCode,
    decimal Amount,
    string Currency,
    string Provider);

public record PaymentVerificationDto(
    Guid PaymentIntentId,
    string Reference,
    PaymentStatus Status,
    string StatusName,
    decimal Amount,
    string Currency,
    string? Channel,
    string? GatewayResponse,
    Guid? TransactionId,
    decimal WalletBalance,
    bool AlreadyCredited);

public record PaymentIntentDto(
    Guid Id,
    Guid UserId,
    Guid WalletId,
    Guid? TransactionId,
    decimal Amount,
    string Currency,
    string Provider,
    string Reference,
    string? AuthorizationUrl,
    PaymentStatus Status,
    string StatusName,
    string? Channel,
    string? GatewayResponse,
    DateTime CreatedAt,
    DateTime? PaidAt,
    DateTime? VerifiedAt)
{
    public static PaymentIntentDto FromEntity(PaymentIntent intent) => new(
        intent.Id,
        intent.UserId,
        intent.WalletId,
        intent.TransactionId,
        intent.Amount,
        intent.Currency,
        intent.Provider,
        intent.Reference,
        intent.AuthorizationUrl,
        intent.Status,
        intent.Status.ToString(),
        intent.Channel,
        intent.GatewayResponse,
        intent.CreatedAt,
        intent.PaidAt,
        intent.VerifiedAt);
}
