using Errandy.Application.Common.DTOs;
using Errandy.Application.Common.Results;
using MediatR;
namespace Errandy.Application.Features.Runners.Commands.ReviewKyc;
/// <summary>
/// Admin decision on a runner's KYC submission. When Approve is false a
/// RejectionReason must be supplied.
/// </summary>
public record ReviewKycCommand(
    Guid AdminId,
    Guid RunnerProfileId,
    bool Approve,
    string? RejectionReason) : IRequest<Result<RunnerProfileDto>>;
