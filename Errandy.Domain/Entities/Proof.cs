namespace Errandy.Domain.Entities;

public class Proof
{
    public Guid Id { get; private set; }
    public Guid ErrandId { get; private set; }
    public string ImageUrl { get; private set; } = string.Empty;
    public string? ReceiptUrl { get; private set; }
    public DateTime UploadedAt { get; private set; }

    private Proof() { }

    public static Proof Create(Guid errandId, string imageUrl, string? receiptUrl, DateTime utcNow)
    {
        if (string.IsNullOrWhiteSpace(imageUrl))
            throw new ArgumentException("At least a proof image is required.", nameof(imageUrl));

        return new Proof
        {
            Id = Guid.NewGuid(),
            ErrandId = errandId,
            ImageUrl = imageUrl,
            ReceiptUrl = receiptUrl,
            UploadedAt = utcNow
        };
    }
}
