using MediatR;

namespace Errandy.Application.Features.Errands.ExpireUnacceptedErrands;

/// <summary>
/// System-triggered sweep: cancels errands still in Created status whose
/// deadline has passed, and refunds the customer. Returns how many were expired.
/// </summary>
public record ExpireUnacceptedErrandsCommand : IRequest<int>;