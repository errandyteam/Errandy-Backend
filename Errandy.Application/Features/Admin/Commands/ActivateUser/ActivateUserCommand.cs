using Errandy.Application.Common.Results;
using MediatR;

namespace Errandy.Application.Features.Admin.Commands.ActivateUser;

public record ActivateUserCommand(Guid UserId) : IRequest<Result>;