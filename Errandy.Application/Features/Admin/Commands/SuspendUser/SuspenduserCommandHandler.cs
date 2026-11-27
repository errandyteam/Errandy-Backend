using Errandy.Application.Common.Interfaces;
using Errandy.Application.Common.Results;
using Errandy.Domain.Enums;
using MediatR;

namespace Errandy.Application.Features.Admin.Commands.SuspendUser;

public class SuspendUserCommandHandler : IRequestHandler<SuspendUserCommand, Result>
{
    private readonly IUserRepository _users;
    private readonly IUnitOfWork _uow;

    public SuspendUserCommandHandler(IUserRepository users, IUnitOfWork uow)
    {
        _users = users;
        _uow = uow;
    }

    public async Task<Result> Handle(SuspendUserCommand request, CancellationToken ct)
    {
        var user = await _users.GetByIdAsync(request.UserId, ct);
        if (user is null)
            return Result.Failure("User not found.", ErrorType.NotFound);

        if (user.Role == UserRole.Admin)
            return Result.Failure("Admins cannot be suspended via this endpoint.", ErrorType.Forbidden);

        if (!user.IsActive)
            return Result.Failure("User is already suspended.", ErrorType.Conflict);

        user.IsActive = false;
        user.UpdatedAt = DateTime.UtcNow;
        _users.Update(user);
        await _uow.SaveChangesAsync(ct);

        return Result.Success();
    }
}