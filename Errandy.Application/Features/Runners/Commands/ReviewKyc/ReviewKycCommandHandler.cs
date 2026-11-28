using Errandy.Application.Common.DTOs;
using Errandy.Application.Common.Interfaces;
using Errandy.Application.Common.Results;
using Errandy.Domain.Entities;
using MediatR;
namespace Errandy.Application.Features.Runners.Commands.ReviewKyc;

public class ReviewKycCommandHandler
    : IRequestHandler<ReviewKycCommand, Result<RunnerProfileDto>>
{
    private readonly IRunnerProfileRepository _profiles;
    private readonly IUnitOfWork _uow;
    public ReviewKycCommandHandler(IRunnerProfileRepository profiles, IUnitOfWork uow)
    {
        _profiles = profiles;
        _uow = uow;
    }
    public async Task<Result<RunnerProfileDto>> Handle(
     ReviewKycCommand request,
     CancellationToken cancellationToken)
    {
        var profile = await _profiles.GetByIdAsync(request.RunnerProfileId, cancellationToken);
        if (profile is null)
            return Result<RunnerProfileDto>.Failure("Runner profile not found.", ErrorType.NotFound);

        if (request.Approve)
        {
            if (!profile.HasSubmittedDocuments)
            {
                return Result<RunnerProfileDto>.Failure(
                    "Cannot approve: the runner has not submitted KYC documents (document type, number, and image are required).",
                    ErrorType.Validation);
            }

            profile.Approve(request.AdminId);
        }
        else
        {
            profile.Reject(request.AdminId,
                string.IsNullOrWhiteSpace(request.RejectionReason)
                    ? "No reason provided."
                    : request.RejectionReason);
        }

        _profiles.Update(profile);
        await _uow.SaveChangesAsync(cancellationToken);

        return Result<RunnerProfileDto>.Success(RunnerProfileDto.FromEntity(profile));
    }
}
