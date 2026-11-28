using System;
using System.Collections.Generic;
using Errandy.Domain.Enums;

namespace Errandy.Application.Common.DTOs;

public record PaymentReconciliationHistoryItemDto(
    Guid AttemptId,
    Guid PaymentIntentId,
    Guid AdminUserId,
    string Reference,
    string Reason,
    PaymentReconciliationOutcome Outcome,
    string OutcomeName,
    string? ResultCode,
    DateTime RequestedAt,
    DateTime? FinishedAt);

public record PaymentReconciliationHistoryPageDto(
    IReadOnlyList<PaymentReconciliationHistoryItemDto> Items,
    int Page,
    int PageSize,
    bool HasMore);
