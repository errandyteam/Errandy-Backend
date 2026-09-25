using Errandy.Application.Common.Exceptions;
using Errandy.Application.Interfaces;
using Errandy.Domain.Enums;
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
    
        var runnerProfile = await _context.RunnerProfiles
            .FirstOrDefaultAsync(rp => rp.UserId == request.RunnerId, cancellationToken);

        if (runnerProfile is null)
            throw new ForbiddenAccessException("No runner profile found for this user. Complete KYC before accepting errands.");

        if (runnerProfile.KycStatus != KycStatus.Approved)
            throw new ForbiddenAccessException($"Runner is not approved to accept errands (KYC status: {runnerProfile.KycStatus}).");

        errand.Accept(request.RunnerId, _dateTimeProvider.UtcNow);

        await _context.SaveChangesAsync(cancellationToken);
    }
}