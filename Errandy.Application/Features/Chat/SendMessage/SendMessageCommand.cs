using MediatR;

namespace Errandy.Application.Features.Chat.SendMessage;

/// <summary>
/// Sends a message on an errand's chat thread. Only the customer or the
/// assigned runner may send (enforced inside Errand.AddMessage()).
/// </summary>
public record SendMessageCommand : IRequest<SendMessageResult>
{
    public Guid ErrandId { get; init; }
    public Guid SenderId { get; init; }
    public string Content { get; init; } = string.Empty;
}

/// <summary>ContentWasMasked is true when a phone number was hidden from the message.</summary>
public record SendMessageResult(Guid MessageId, bool ContentWasMasked);