using Errandy.Application.Common.DTOs;
using Errandy.Application.Common.Results;
using MediatR;

namespace Errandy.Application.Features.Auth.Commands.RefreshTokens;

public record RefreshTokenCommand(string RefreshToken) : IRequest<Result<AuthResponseDto>>;