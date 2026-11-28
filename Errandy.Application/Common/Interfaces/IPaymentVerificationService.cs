using System;
using System.Threading;
using System.Threading.Tasks;
using Errandy.Application.Common.DTOs;
using Errandy.Application.Common.Results;

namespace Errandy.Application.Common.Interfaces;

public interface IPaymentVerificationService
{
    // userId must come from a trusted server-side source, never directly from an API request.
    Task<Result<PaymentVerificationDto>> VerifyOwnedPaymentAsync(
        string reference,
        Guid userId,
        CancellationToken cancellationToken = default);
}
