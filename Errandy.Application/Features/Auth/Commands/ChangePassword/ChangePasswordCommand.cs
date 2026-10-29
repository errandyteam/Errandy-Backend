using Errandy.Application.Common.Results;
using MediatR;   

namespace Errandy.Application.Features.Auth.Commands.ChangePassword;

public record ChangePasswordCommand(
    Guid UserId,
    string CurrentPassword,
    string NewPassword) : IRequest<Result>;