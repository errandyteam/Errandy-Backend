using Errandy.Application.Common.Results;
using MediatR;

namespace Errandy.Application.Features.Auth.Commands.Logout;

public record LogoutCommand(string? RefreshToken) : IRequest<Result>;