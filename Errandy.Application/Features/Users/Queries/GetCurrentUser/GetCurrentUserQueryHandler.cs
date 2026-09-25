using Errandy.Application.Common.DTOs;
using Errandy.Application.Common.Interfaces;
using Errandy.Application.Common.Results;
using MediatR;
namespace Errandy.Application.Features.Users.Queries.GetCurrentUser;

public class GetCurrentUserQueryHandler
    : IRequestHandler<GetCurrentUserQuery, Result<UserDto>>
{
    private readonly IUserRepository _users;
    public GetCurrentUserQueryHandler(IUserRepository users) => _users = users;
    public async Task<Result<UserDto>> Handle(
        GetCurrentUserQuery request,
        CancellationToken cancellationToken)
    {
        var user = await _users.GetByIdAsync(request.UserId, cancellationToken);
        if (user is null)
            return Result<UserDto>.Failure("User not found.", ErrorType.NotFound);
        return Result<UserDto>.Success(UserDto.FromEntity(user));
    }
}
