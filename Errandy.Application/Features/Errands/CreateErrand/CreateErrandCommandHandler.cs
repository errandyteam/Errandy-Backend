using Errandy.Application.Interfaces;
using Errandy.Domain.Entities;
using MediatR;

namespace Errandy.Application.Features.Errands.CreateErrand;

public class CreateErrandCommandHandler : IRequestHandler<CreateErrandCommand, Guid>
{
    private readonly IApplicationDbContext _context;
    private readonly IEscrowService _escrowService;
    private readonly IDateTimeProvider _dateTimeProvider;

    public CreateErrandCommandHandler(
        IApplicationDbContext context,
        IEscrowService escrowService,
        IDateTimeProvider dateTimeProvider)
    {
        _context = context;
        _escrowService = escrowService;
        _dateTimeProvider = dateTimeProvider;
    }

    public async Task<Guid> Handle(CreateErrandCommand request, CancellationToken cancellationToken)
    {
        var utcNow = _dateTimeProvider.UtcNow;

        var errand = Errand.Create(
            customerId: request.CustomerId,
            category: request.Category,
            description: request.Description,
            estimatedCost: request.EstimatedCost,
            pickupLatitude: request.PickupLatitude,
            pickupLongitude: request.PickupLongitude,
            deadline: request.Deadline,
            utcNow: utcNow);

        _context.Errands.Add(errand);

        // Save first so the Errand row exists before Engineer B's escrow
        // system tries to reference ErrandId as a foreign key.
        await _context.SaveChangesAsync(cancellationToken);

        // Lock funds in escrow. Buffer calculation (10-20% per PRD 6.1) is
        // Engineer B's responsibility inside the implementation of this method.
        await _escrowService.LockFundsAsync(errand.Id, request.CustomerId, request.EstimatedCost, cancellationToken);

        return errand.Id;
    }
}
