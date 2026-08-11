using Errandy.Application.Common.DTOs;
using Errandy.Application.Common.Interfaces;
using Errandy.Application.Common.Results;
using MediatR;
namespace Errandy.Application.Features.Runners.Queries.GetPendingKyc;

public class GetPendingKycQueryHandler
    : IRequestHandler<GetPendingKycQuery, Result<IReadOnlyList<RunnerProfileDto>>>
{
    private readonly IRunnerProfileRepository _profiles;
    public GetPendingKycQueryHandler(IRunnerProfileRepository profiles)
        => _profiles = profiles;
    public async Task<Result<IReadOnlyList<RunnerProfileDto>>> Handle(
        GetPendingKycQuery request,
        CancellationToken cancellationToken)
    {
        var pending = await _profiles.GetPendingAsync(cancellationToken);
        IReadOnlyList<RunnerProfileDto> dtos =
            pending.Select(RunnerProfileDto.FromEntity).ToList();
        return Result<IReadOnlyList<RunnerProfileDto>>.Success(dtos);
    }
}