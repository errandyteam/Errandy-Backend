using System;
using System.Collections.Generic;

namespace Errandy.Application.Common.DTOs;

// Operational information only: checkout URLs, access codes and customer email
// are intentionally omitted from the review queue.
public record PendingPaymentReviewDto(
    Guid PaymentIntentId,
    string Reference,
    Guid UserId,
    Guid WalletId,
    decimal Amount,
    string Currency,
    string Provider,
    DateTime CreatedAt,
    bool CheckoutInitialized);

public record PendingPaymentReviewPageDto(
    IReadOnlyList<PendingPaymentReviewDto> Items,
    int Page,
    int PageSize,
    bool HasMore);
