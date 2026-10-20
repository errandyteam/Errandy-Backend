using Errandy.Application.Common.DTOs;
using Errandy.Application.Common.Interfaces;
using Errandy.Application.Common.Results;
using MediatR;

namespace Errandy.Application.Features.Runners.Queries.GetMyRunnerProfile;

public class GetMyRunnerProfileQueryHandler
    : IRequestHandler<GetMyRunnerProfileQuery, Result<RunnerProfileDto>>
{
    private readonly IRunnerProfileRepository _profiles;

    public GetMyRunnerProfileQueryHandler(IRunnerProfileRepository profiles)
        => _profiles = profiles;

    public async Task<Result<RunnerProfileDto>> Handle(
        GetMyRunnerProfileQuery request,
        CancellationToken cancellationToken)
    {
        var profile = await _profiles.GetByUserIdAsync(request.UserId, cancellationToken);

        if (profile is null)
            return Result<RunnerProfileDto>.Failure(
                "Runner profile not found. Call POST /api/Runner/become to create one.",
                ErrorType.NotFound);

        return Result<RunnerProfileDto>.Success(RunnerProfileDto.FromEntity(profile));
    }
}