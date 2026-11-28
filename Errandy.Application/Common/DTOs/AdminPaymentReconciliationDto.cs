using System;

namespace Errandy.Application.Common.DTOs;

public record AdminPaymentReconciliationDto(
    Guid AuditAttemptId,
    PaymentVerificationDto Payment);
