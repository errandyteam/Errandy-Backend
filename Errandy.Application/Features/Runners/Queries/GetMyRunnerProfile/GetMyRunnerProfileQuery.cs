using Errandy.Application.Common.DTOs;
using Errandy.Application.Common.Results;
using MediatR;

namespace Errandy.Application.Features.Runners.Queries.GetMyRunnerProfile;

public record GetMyRunnerProfileQuery(Guid UserId) : IRequest<Result<RunnerProfileDto>>;