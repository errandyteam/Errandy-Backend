using Errandy.Application.Common.DTOs;
using Errandy.Application.Common.Results;
using MediatR;
namespace Errandy.Application.Features.Runners.Commands.SubmitKyc;

public record SubmitKycCommand(
    Guid UserId,
    string DocumentType,
    string DocumentNumber,
    string DocumentImageUrl) : IRequest<Result<RunnerProfileDto>>;
