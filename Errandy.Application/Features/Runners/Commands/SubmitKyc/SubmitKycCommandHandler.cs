using Errandy.Application.Common.DTOs;
using Errandy.Application.Common.Interfaces;
using Errandy.Application.Common.Results;
using Errandy.Domain.Enums;
using MediatR;
namespace Errandy.Application.Features.Runners.Commands.SubmitKyc;

public class SubmitKycCommandHandler
    : IRequestHandler<SubmitKycCommand, Result<RunnerProfileDto>>
{
    private readonly IRunnerProfileRepository _profiles;
    private readonly IUnitOfWork _uow;
    public SubmitKycCommandHandler(IRunnerProfileRepository profiles, IUnitOfWork uow)
    {
        _profiles = profiles;
        _uow = uow;
    }
    public async Task<Result<RunnerProfileDto>> Handle(
        SubmitKycCommand request,
        CancellationToken cancellationToken)
    {
        var profile = await _profiles.GetByUserIdAsync(request.UserId, cancellationToken);
        if (profile is null)
            return Result<RunnerProfileDto>.Failure(
                "Runner profile not found. Become a runner first.", ErrorType.NotFound);
        if (profile.KycStatus == KycStatus.Approved)
            return Result<RunnerProfileDto>.Failure(
                "KYC has already been approved.", ErrorType.Conflict);
        profile.DocumentType = request.DocumentType;
        profile.DocumentNumber = request.DocumentNumber;
        profile.DocumentImageUrl = request.DocumentImageUrl;
        profile.KycStatus = KycStatus.Pending; // (re)submission resets to pending
        profile.RejectionReason = null;
        profile.UpdatedAt = DateTime.UtcNow;
        _profiles.Update(profile);
        await _uow.SaveChangesAsync(cancellationToken);
        return Result<RunnerProfileDto>.Success(RunnerProfileDto.FromEntity(profile));
    }
}
