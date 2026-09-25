using Errandy.Application.DTOs;
using MediatR;

namespace Errandy.Application.Features.Chat.GetMessages;

/// <summary>
/// Fetches the full chat thread for one errand, oldest first. Authorization
/// (only customer/runner on this errand can view) is enforced in the handler.
/// </summary>
public record GetMessagesQuery : IRequest<List<MessageDto>>
{
    public Guid ErrandId { get; init; }
    public Guid RequestingUserId { get; init; }
}
