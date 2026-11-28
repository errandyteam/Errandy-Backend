using MediatR;

namespace Errandy.Application.Features.Errands.GetErrandsDueForAutoRelease;

/// <summary>IDs of errands whose customer-confirmation window has passed, oldest first.</summary>
public record GetErrandsDueForAutoReleaseQuery : IRequest<List<Guid>>;