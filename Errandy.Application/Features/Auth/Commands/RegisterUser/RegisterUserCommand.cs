using Errandy.Application.Common.DTOs;
using Errandy.Application.Common.Results;
using MediatR;
namespace Errandy.Application.Features.Auth.Commands.RegisterUser;

public record RegisterUserCommand(
    string FullName,
    string Email,
    string PhoneNumber,
    string Password) : IRequest<Result<AuthResponseDto>>;
