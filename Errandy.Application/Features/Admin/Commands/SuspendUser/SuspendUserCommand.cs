using Errandy.Application.Common.Results;
using MediatR;

namespace Errandy.Application.Features.Admin.Commands.SuspendUser;

public record SuspendUserCommand(
    Guid UserId,
    string? Reason) : IRequest<Result>;