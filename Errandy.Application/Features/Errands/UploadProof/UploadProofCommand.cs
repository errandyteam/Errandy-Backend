using MediatR;

namespace Errandy.Application.Features.Errands.UploadProof;

/// <summary>
/// Runner submits proof of completion as real files. The photo is required, the receipt is optional.
/// The streams must be seekable (uploaded form files are).
/// </summary>
public record UploadProofCommand : IRequest
{
    public Guid ErrandId { get; init; }
    public Guid RunnerId { get; init; }
    public Stream ImageContent { get; init; } = Stream.Null;
    public Stream? ReceiptContent { get; init; }
    public decimal FinalCost { get; init; }
}