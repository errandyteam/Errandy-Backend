using System;
using System.Collections.Generic;
using Errandy.Domain.Enums;

namespace Errandy.Application.Common.DTOs;

// Operational data only: no lease tokens, checkout URLs, access codes,
// customer email, or raw gateway responses.
public record AutomaticPaymentReviewDto(
    Guid CheckId,
    Guid PaymentIntentId,
    string Reference,
    Guid UserId,
    decimal Amount,
    string Currency,
    PaymentStatus PaymentStatus,
    string PaymentStatusName,
    int AttemptsStarted,
    int MaximumAttempts,
    AutomaticPaymentCheckResult LastResult,
    string LastResultName,
    DateTime? LastStartedAt,
    DateTime? LastFinishedAt);

public record AutomaticPaymentReviewPageDto(
    IReadOnlyList<AutomaticPaymentReviewDto> Items,
    int Page,
    int PageSize,
    bool HasMore);
