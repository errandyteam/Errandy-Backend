using Errandy.Application.DTOs;
using MediatR;

namespace Errandy.Application.Features.Disputes.GetOpenDisputes;

/// <summary>Admin work queue: every unresolved dispute, oldest first.</summary>
public record GetOpenDisputesQuery : IRequest<List<DisputeSummaryDto>>;