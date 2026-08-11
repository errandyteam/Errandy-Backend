using Errandy.Application.Common.DTOs;
using Errandy.Application.Common.Results;
using MediatR;
namespace Errandy.Application.Features.Runners.Commands.BecomeRunner;
/// <summary>
/// Upgrades an authenticated customer to a runner and creates a pending
/// runner profile ready for KYC submission.
/// </summary>
public record BecomeRunnerCommand(Guid UserId) : IRequest<Result<RunnerProfileDto>>;