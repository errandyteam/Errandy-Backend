using Errandy.Application.Common.DTOs;
using Errandy.Application.Common.Interfaces;
using Errandy.Application.Common.Results;
using Errandy.Domain.Enums;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace Errandy.Application.Features.Runners.Queries.GetPendingKyc;

public class GetPendingKycQueryHandler
    : IRequestHandler<GetPendingKycQuery, Result<IReadOnlyList<RunnerProfileDto>>>
{
    private readonly IRunnerProfileRepository _profiles;

    public GetPendingKycQueryHandler(IRunnerProfileRepository profiles)
        => _profiles = profiles;

    public async Task<Result<IReadOnlyList<RunnerProfileDto>>> Handle(
        GetPendingKycQuery request,
        CancellationToken ct)
    {
        // The repository already returns only Pending profiles with documents on file.
        var pending = await _profiles.GetPendingAsync(ct);

        var dtos = pending
            .Select(RunnerProfileDto.FromEntity)
            .ToList()
            .AsReadOnly();

        return Result<IReadOnlyList<RunnerProfileDto>>.Success(dtos);
    }
}