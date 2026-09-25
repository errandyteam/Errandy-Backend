using Errandy.Application.DTOs;
using MediatR;

namespace Errandy.Application.Features.Errands.GetErrandById;

public record GetErrandByIdQuery : IRequest<ErrandDto>
{
    public Guid ErrandId { get; init; }
    public Guid RequestingUserId { get; init; }
}