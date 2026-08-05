using MediatR;

namespace Errandy.Application.Features.Errands.ConfirmCompletion;

/// <summary>
/// Customer confirms the runner's proof was satisfactory. This is the trigger
/// point that releases escrow funds to the runner (PRD 6.2). The same
/// underlying logic should be reused by the future auto-release background
/// job (customer has 2-6 hours to respond before auto-release fires).
/// </summary>
public record ConfirmCompletionCommand : IRequest
{
    public Guid ErrandId { get; init; }
    public Guid CustomerId { get; init; }
}
