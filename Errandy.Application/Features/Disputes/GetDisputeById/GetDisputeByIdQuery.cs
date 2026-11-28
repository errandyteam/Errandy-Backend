using Errandy.Application.DTOs;
using MediatR;

namespace Errandy.Application.Features.Disputes.GetDisputeById;

public record GetDisputeByIdQuery : IRequest<DisputeDto>
{
    public Guid DisputeId { get; init; }
    public Guid RequestingUserId { get; init; }
    public bool IsAdmin { get; init; }
}