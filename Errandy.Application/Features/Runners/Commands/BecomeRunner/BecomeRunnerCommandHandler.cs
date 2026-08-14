using Errandy.Application.Common.DTOs;
using Errandy.Application.Common.Interfaces;
using Errandy.Application.Common.Results;
using Errandy.Domain.Entities;
using Errandy.Domain.Enums;
using MediatR;
namespace Errandy.Application.Features.Runners.Commands.BecomeRunner;

public class BecomeRunnerCommandHandler
    : IRequestHandler<BecomeRunnerCommand, Result<RunnerProfileDto>>
{
    private readonly IUserRepository _users;
    private readonly IRunnerProfileRepository _profiles;
    private readonly IUnitOfWork _uow;
    public BecomeRunnerCommandHandler(
        IUserRepository users,
        IRunnerProfileRepository profiles,
        IUnitOfWork uow)
    {
        _users = users;
        _profiles = profiles;
        _uow = uow;
    }
    public async Task<Result<RunnerProfileDto>> Handle(
        BecomeRunnerCommand request,
        CancellationToken cancellationToken)
    {
        var user = await _users.GetByIdAsync(request.UserId, cancellationToken);
        if (user is null)
            return Result<RunnerProfileDto>.Failure("User not found.", ErrorType.NotFound);
        if (user.Role == UserRole.Admin)
            return Result<RunnerProfileDto>.Failure(
                "Admins cannot become runners.", ErrorType.Forbidden);
        var existing = await _profiles.GetByUserIdAsync(user.Id, cancellationToken);
        if (existing is not null)
            return Result<RunnerProfileDto>.Failure(
                "You already have a runner profile.", ErrorType.Conflict);
        var profile = new RunnerProfile
        {
            UserId = user.Id,
            KycStatus = KycStatus.Pending
        };
        user.Role = UserRole.Runner;
        _users.Update(user);
        await _profiles.AddAsync(profile, cancellationToken);
        await _uow.SaveChangesAsync(cancellationToken);
        return Result<RunnerProfileDto>.Success(RunnerProfileDto.FromEntity(profile));
    }
}