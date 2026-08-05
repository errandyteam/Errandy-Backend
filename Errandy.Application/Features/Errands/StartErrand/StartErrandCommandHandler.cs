using Errandy.Application.Common.Exceptions;
using Errandy.Application.Interfaces;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace Errandy.Application.Features.Errands.StartErrand;

public class StartErrandCommandHandler : IRequestHandler<StartErrandCommand>
{
    private readonly IApplicationDbContext _context;
    private readonly IDateTimeProvider _dateTimeProvider;

    public StartErrandCommandHandler(IApplicationDbContext context, IDateTimeProvider dateTimeProvider)
    {
        _context = context;
        _dateTimeProvider = dateTimeProvider;
    }

    public async Task Handle(StartErrandCommand request, CancellationToken cancellationToken)
    {
        var errand = await _context.Errands
            .FirstOrDefaultAsync(e => e.Id == request.ErrandId, cancellationToken);

        if (errand is null)
            throw new NotFoundException(nameof(Domain.Entities.Errand), request.ErrandId);

        errand.Start(request.RunnerId, _dateTimeProvider.UtcNow);

        await _context.SaveChangesAsync(cancellationToken);
    }
}