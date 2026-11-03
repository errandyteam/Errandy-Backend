using Errandy.Application.Common.DTOs;
using Errandy.Application.Common.Results;
using MediatR;

namespace Errandy.Application.Features.Users.Commands.UpdateProfile;

public record UpdateProfileCommand(
    Guid UserId,
    string FullName,
    string PhoneNumber) : IRequest<Result<UserDto>>;