using MediatR;

namespace Errandy.Application.Features.Chat.SendMessage;

/// <summary>
/// Sends a message on an errand's chat thread. Only the customer or the
/// assigned runner on that errand may send — enforced inside Errand.AddMessage().
/// </summary>
public record SendMessageCommand : IRequest<Guid>
{
    public Guid ErrandId { get; init; }
    public Guid SenderId { get; init; }
    public string Content { get; init; } = string.Empty;
}
