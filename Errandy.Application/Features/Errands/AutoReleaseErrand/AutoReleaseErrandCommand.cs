using MediatR;

namespace Errandy.Application.Features.Errands.AutoReleaseErrand;

/// <summary>
/// System-triggered version of ConfirmCompletion — fires when the customer
/// doesn't respond within the window (PRD: 2-6 hours). No CustomerId
/// ownership check since there's no human caller; the handler instead
/// checks the errand is still PendingConfirmation (a no-op if the customer
/// already confirmed or disputed before this job ran).
/// </summary>
public record AutoReleaseErrandCommand : IRequest
{
    public Guid ErrandId { get; init; }
}