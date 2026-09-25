using MediatR;

namespace Errandy.Application.Features.Errands.UploadProof;

/// <summary>
/// Runner uploads proof of completion (photo + optional receipt) and states
/// the final cost. Moves the errand to PendingConfirmation — does NOT
/// release any funds (that only happens on customer confirmation or
/// auto-release).
/// </summary>
public record UploadProofCommand : IRequest
{
    public Guid ErrandId { get; init; }
    public Guid RunnerId { get; init; }
    public string ImageUrl { get; init; } = string.Empty;
    public string? ReceiptUrl { get; init; }
    public decimal FinalCost { get; init; }
}
