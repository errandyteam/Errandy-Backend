using Errandy.Application.Common.DTOs;
using Errandy.Application.Common.Results;
using MediatR;
namespace Errandy.Application.Features.Auth.Commands.LoginUser;

public record LoginUserCommand(string Email, string Password)
    : IRequest<Result<AuthResponseDto>>;
