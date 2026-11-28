using Errandy.Application.Common.Exceptions;
using Errandy.Application.Interfaces;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace Errandy.Application.Features.Errands.AcceptErrand;

public class AcceptErrandCommandHandler : IRequestHandler<AcceptErrandCommand>
{
    private readonly IApplicationDbContext _context;
    private readonly IDateTimeProvider _dateTimeProvider;

    public AcceptErrandCommandHandler(IApplicationDbContext context, IDateTimeProvider dateTimeProvider)
    {
        _context = context;
        _dateTimeProvider = dateTimeProvider;
    }

    public async Task Handle(AcceptErrandCommand request, CancellationToken cancellationToken)
    {
        var errand = await _context.Errands
            .FirstOrDefaultAsync(e => e.Id == request.ErrandId, cancellationToken);

        if (errand is null)
            throw new NotFoundException(nameof(Domain.Entities.Errand), request.ErrandId);

        if (errand.CustomerId == request.RunnerId)
            throw new ForbiddenAccessException("You cannot accept your own errand.");

        var runnerProfile = await _context.RunnerProfiles
            .FirstOrDefaultAsync(rp => rp.UserId == request.RunnerId, cancellationToken);

        if (runnerProfile is null || !runnerProfile.IsEligibleToAcceptErrands)
            throw new ForbiddenAccessException("Only runners with approved KYC can accept errands.");

        errand.Accept(request.RunnerId, _dateTimeProvider.UtcNow);

        await _context.SaveChangesAsync(cancellationToken);
    }
}