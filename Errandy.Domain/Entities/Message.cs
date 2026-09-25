namespace Errandy.Domain.Entities;

public class Message
{
    public Guid Id { get; private set; }
    public Guid ErrandId { get; private set; }
    public Guid SenderId { get; private set; }
    public string Content { get; private set; } = string.Empty;
    public DateTime CreatedAt { get; private set; }

    private Message() { }

    internal static Message Create(Guid errandId, Guid senderId, string content, DateTime utcNow)
    {
        if (string.IsNullOrWhiteSpace(content))
            throw new ArgumentException("Message content cannot be empty.", nameof(content));

        if (content.Length > 2000)
            throw new ArgumentException("Message content exceeds 2000 characters.", nameof(content));

        return new Message
        {
            Id = Guid.NewGuid(),
            ErrandId = errandId,
            SenderId = senderId,
            Content = content.Trim(),
            CreatedAt = utcNow
        };
    }
}
