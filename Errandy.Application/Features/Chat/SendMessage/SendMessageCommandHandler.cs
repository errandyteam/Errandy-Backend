using Errandy.Application.Common.Exceptions;
using Errandy.Application.Common.Utils;
using Errandy.Application.Interfaces;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace Errandy.Application.Features.Chat.SendMessage;

public class SendMessageCommandHandler : IRequestHandler<SendMessageCommand, SendMessageResult>
{
    private readonly IApplicationDbContext _context;
    private readonly IDateTimeProvider _dateTimeProvider;

    public SendMessageCommandHandler(IApplicationDbContext context, IDateTimeProvider dateTimeProvider)
    {
        _context = context;
        _dateTimeProvider = dateTimeProvider;
    }

    public async Task<SendMessageResult> Handle(SendMessageCommand request, CancellationToken cancellationToken)
    {
        var errand = await _context.Errands
            .FirstOrDefaultAsync(e => e.Id == request.ErrandId, cancellationToken);

        if (errand is null)
            throw new NotFoundException(nameof(Domain.Entities.Errand), request.ErrandId);

        // Phone numbers are hidden BEFORE saving, so they never reach the
        // database or the other person.
        var safeContent = PhoneNumberMasker.Mask(request.Content, out var wasMasked);

        // errand.AddMessage() checks the sender is the customer or the assigned runner.
        var message = errand.AddMessage(request.SenderId, safeContent, _dateTimeProvider.UtcNow);

        _context.Messages.Add(message);
        await _context.SaveChangesAsync(cancellationToken);

        return new SendMessageResult(message.Id, wasMasked);
    }
}