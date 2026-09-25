using Errandy.Domain.Enums;
using Errandy.Domain.Exceptions;

namespace Errandy.Domain.Entities;

public class Dispute
{
    public Guid Id { get; private set; }
    public Guid ErrandId { get; private set; }
    public Guid RaisedBy { get; private set; }
    public DisputeReason Reason { get; private set; }
    public string? Description { get; private set; }
    public DisputeStatus Status { get; private set; }

    private readonly List<string> _evidenceUrls = new();
    public IReadOnlyCollection<string> EvidenceUrls => _evidenceUrls.AsReadOnly();

    public DisputeResolutionType Resolution { get; private set; } = DisputeResolutionType.None;
    public string? ResolutionNotes { get; private set; }
    public decimal? ResolutionAmount { get; private set; }
    public Guid? ResolvedByAdminId { get; private set; }

    public DateTime CreatedAt { get; private set; }
    public DateTime? ResolvedAt { get; private set; }

    private Dispute() { }

    public static Dispute Create(
        Guid errandId,
        Guid raisedBy,
        DisputeReason reason,
        string? description,
        IEnumerable<string> evidenceUrls,
        DateTime utcNow)
    {
        var dispute = new Dispute
        {
            Id = Guid.NewGuid(),
            ErrandId = errandId,
            RaisedBy = raisedBy,
            Reason = reason,
            Description = description,
            Status = DisputeStatus.Open,
            CreatedAt = utcNow
        };

        dispute._evidenceUrls.AddRange(evidenceUrls.Where(u => !string.IsNullOrWhiteSpace(u)));
        return dispute;
    }

    public void AddEvidence(string url)
    {
        if (Status is DisputeStatus.Resolved or DisputeStatus.Rejected)
            throw new InvalidOperationException("Cannot add evidence to a closed dispute.");

        if (!string.IsNullOrWhiteSpace(url))
            _evidenceUrls.Add(url);
    }

    public void BeginReview()
    {
        if (Status != DisputeStatus.Open)
            throw new InvalidOperationException($"Cannot begin review from status '{Status}'.");

        Status = DisputeStatus.UnderReview;
    }

    /// <summary>
    /// Admin resolves the dispute (PRD 12.3 — full payment / partial refund / penalty).
    /// This only records the decision; the ResolveDisputeCommandHandler is
    /// responsible for calling IEscrowService to actually move money.
    /// </summary>
    public void Resolve(
        Guid adminId,
        DisputeResolutionType resolution,
        decimal? resolutionAmount,
        string? notes,
        DateTime utcNow)
    {
        if (Status is DisputeStatus.Resolved or DisputeStatus.Rejected)
            throw new InvalidOperationException("Dispute has already been closed.");

        if (resolution == DisputeResolutionType.None)
            throw new ArgumentException("A concrete resolution type is required.", nameof(resolution));

        ResolvedByAdminId = adminId;
        Resolution = resolution;
        ResolutionAmount = resolutionAmount;
        ResolutionNotes = notes;
        Status = DisputeStatus.Resolved;
        ResolvedAt = utcNow;
    }
}
