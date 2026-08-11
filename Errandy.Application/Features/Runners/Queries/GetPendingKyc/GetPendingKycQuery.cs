using Errandy.Application.Common.DTOs;
using Errandy.Application.Common.Results;
using MediatR;
namespace Errandy.Application.Features.Runners.Queries.GetPendingKyc;

public record GetPendingKycQuery : IRequest<Result<IReadOnlyList<RunnerProfileDto>>>;
