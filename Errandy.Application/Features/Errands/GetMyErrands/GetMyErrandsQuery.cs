using Errandy.Application.DTOs;
using Errandy.Domain.Enums;
using MediatR;

namespace Errandy.Application.Features.Errands.GetMyErrands;

public record GetMyErrandsQuery : IRequest<List<ErrandSummaryDto>>
{
    public Guid UserId { get; init; }
    public bool AsCustomer { get; init; } // true = errands they created, false = errands they're running
    public ErrandStatus? StatusFilter { get; init; }
}