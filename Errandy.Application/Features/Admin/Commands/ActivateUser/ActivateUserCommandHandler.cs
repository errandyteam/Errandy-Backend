using Errandy.Application.Common.Interfaces;
using Errandy.Application.Common.Results;
using MediatR;

namespace Errandy.Application.Features.Admin.Commands.ActivateUser;

public class ActivateUserCommandHandler : IRequestHandler<ActivateUserCommand, Result>
{
    private readonly IUserRepository _users;
    private readonly IUnitOfWork _uow;

    public ActivateUserCommandHandler(IUserRepository users, IUnitOfWork uow)
    {
        _users = users;
        _uow = uow;
    }

    public async Task<Result> Handle(ActivateUserCommand request, CancellationToken ct)
    {
        var user = await _users.GetByIdAsync(request.UserId, ct);
        if (user is null)
            return Result.Failure("User not found.", ErrorType.NotFound);

        if (user.IsActive)
            return Result.Failure("User is already active.", ErrorType.Conflict);

        user.IsActive = true;
        user.UpdatedAt = DateTime.UtcNow;
        _users.Update(user);
        await _uow.SaveChangesAsync(ct);

        return Result.Success();
    }
}