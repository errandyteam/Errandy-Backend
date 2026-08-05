using Errandy.Application.Common.Exceptions;
using Errandy.Application.DTOs;
using Errandy.Application.Interfaces;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace Errandy.Application.Features.Chat.GetMessages;

public class GetMessagesQueryHandler : IRequestHandler<GetMessagesQuery, List<MessageDto>>
{
    private readonly IApplicationDbContext _context;

    public GetMessagesQueryHandler(IApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<List<MessageDto>> Handle(GetMessagesQuery request, CancellationToken cancellationToken)
    {
        var errand = await _context.Errands
            .FirstOrDefaultAsync(e => e.Id == request.ErrandId, cancellationToken);

        if (errand is null)
            throw new NotFoundException(nameof(Domain.Entities.Errand), request.ErrandId);

        // Chat is private between the customer and the assigned runner —
        // nobody else (including other runners who haven't accepted it) can read it.
        if (request.RequestingUserId != errand.CustomerId && request.RequestingUserId != errand.RunnerId)
            throw new ForbiddenAccessException("Only the customer or assigned runner may view this chat.");

        return await _context.Messages
            .Where(m => m.ErrandId == request.ErrandId)
            .OrderBy(m => m.CreatedAt)
            .Select(m => new MessageDto
            {
                Id = m.Id,
                ErrandId = m.ErrandId,
                SenderId = m.SenderId,
                Content = m.Content,
                CreatedAt = m.CreatedAt
            })
            .ToListAsync(cancellationToken);
    }
}
