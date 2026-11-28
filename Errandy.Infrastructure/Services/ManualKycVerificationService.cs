using Errandy.Application.Common.Interfaces;
using Errandy.Domain.Entities;
using Errandy.Domain.Enums;

namespace Errandy.Infrastructure.Services;

/// <summary>
/// No automated check. Every submission waits for admin review.
/// </summary>
public sealed class ManualKycVerificationService : IKycVerificationService
{
    public Task<KycVerificationResult> VerifyAsync(RunnerProfile profile, CancellationToken ct) =>
        Task.FromResult(new KycVerificationResult(true, KycStatus.Pending));
}