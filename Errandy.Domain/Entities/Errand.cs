using Errandy.Domain.Enums;
using Errandy.Domain.Exceptions;


namespace Errandy.Domain.Entities;

/// <summary>
/// Aggregate root for the errand lifecycle. All state transitions live here so
/// that "Enforce valid transitions" (PRD requirement, Engineer C scope) can never
/// be bypassed by calling code — Application layer handlers call these methods,
/// they never set Status directly.
///
/// Note: this entity does NOT touch money. It raises the *intent* (via the
/// IEscrowService contract, invoked from the command handlers) for Engineer B's
/// wallet/escrow system to act on. Owning that boundary is what keeps C and B decoupled.
/// </summary>
public class Errand
{
    public Guid Id { get; private set; }
    public Guid CustomerId { get; private set; }
    public Guid? RunnerId { get; private set; }

    public ErrandCategory Category { get; private set; }
    public string Description { get; private set; } = string.Empty;
    public ErrandStatus Status { get; private set; }

    public decimal EstimatedCost { get; private set; }
    public decimal? FinalCost { get; private set; }

    public double PickupLatitude { get; private set; }
    public double PickupLongitude { get; private set; }

    public DateTime? Deadline { get; private set; }

    public DateTime CreatedAt { get; private set; }
    public DateTime? AcceptedAt { get; private set; }
    public DateTime? StartedAt { get; private set; }
    public DateTime? PendingConfirmationAt { get; private set; }
    public DateTime? CompletedAt { get; private set; }
    public DateTime? CancelledAt { get; private set; }

    private readonly List<Message> _messages = new();
    public IReadOnlyCollection<Message> Messages => _messages.AsReadOnly();

    public Proof? Proof { get; private set; }
    public Dispute? Dispute { get; private set; }

    // EF Core requires a parameterless constructor.
    private Errand() { }

    public static Errand Create(
        Guid customerId,
        ErrandCategory category,
        string description,
        decimal estimatedCost,
        double pickupLatitude,
        double pickupLongitude,
        DateTime? deadline,
        DateTime utcNow)
    {
        if (customerId == Guid.Empty)
            throw new ArgumentException("CustomerId is required.", nameof(customerId));

        if (string.IsNullOrWhiteSpace(description))
            throw new ArgumentException("Description is required.", nameof(description));

        if (estimatedCost <= 0)
            throw new ArgumentException("EstimatedCost must be greater than zero.", nameof(estimatedCost));

        return new Errand
        {
            Id = Guid.NewGuid(),
            CustomerId = customerId,
            Category = category,
            Description = description.Trim(),
            EstimatedCost = estimatedCost,
            PickupLatitude = pickupLatitude,
            PickupLongitude = pickupLongitude,
            Deadline = deadline,
            Status = ErrandStatus.Created,
            CreatedAt = utcNow
        };
    }

    public void Accept(Guid runnerId, DateTime utcNow)
    {
        EnsureStatus(ErrandStatus.Created, nameof(Accept));

        RunnerId = runnerId;
        Status = ErrandStatus.Accepted;
        AcceptedAt = utcNow;
    }

    public void Start(Guid runnerId, DateTime utcNow)
    {
        EnsureStatus(ErrandStatus.Accepted, nameof(Start));
        EnsureRunnerOwnership(runnerId);

        Status = ErrandStatus.InProgress;
        StartedAt = utcNow;
    }

    /// <summary>
    /// Runner uploads proof of completion. This moves the errand into
    /// PendingConfirmation — it does NOT release funds. Only the customer's
    /// ConfirmCompletion (or the auto-release background job) does that.
    /// </summary>
    public void AttachProofAndSubmitForConfirmation(Guid runnerId, Proof proof, decimal finalCost, DateTime utcNow)
    {
        EnsureStatus(ErrandStatus.InProgress, nameof(AttachProofAndSubmitForConfirmation));
        EnsureRunnerOwnership(runnerId);

        if (finalCost <= 0)
            throw new ArgumentException("FinalCost must be greater than zero.", nameof(finalCost));

        Proof = proof;
        FinalCost = finalCost;
        Status = ErrandStatus.PendingConfirmation;
        PendingConfirmationAt = utcNow;
    }

    /// <summary>
    /// Customer confirms (or auto-release job fires). Marks Completed.
    /// The caller (command handler) is responsible for invoking
    /// IEscrowService.ReleaseFundsAsync — this method only updates errand state.
    /// </summary>
    public void ConfirmCompletion(DateTime utcNow)
    {
        EnsureStatus(ErrandStatus.PendingConfirmation, nameof(ConfirmCompletion));

        Status = ErrandStatus.Completed;
        CompletedAt = utcNow;
    }

    public void RaiseDispute(Dispute dispute)
    {
        if (Status != ErrandStatus.PendingConfirmation && Status != ErrandStatus.InProgress)
            throw new InvalidErrandStateException(Status, nameof(RaiseDispute));

        if (Dispute is not null)
            throw new InvalidOperationException("This errand already has an open dispute.");

        Dispute = dispute;
        Status = ErrandStatus.Disputed;
    }

    /// <summary>
    /// Called after Admin resolves the dispute (PRD 12.3). Errand moves to
    /// Completed or Cancelled depending on resolution; escrow release/refund
    /// itself is triggered by the ResolveDisputeCommandHandler via IEscrowService.
    /// </summary>
    public void ResolveDisputeAndClose(bool errandConsideredComplete, DateTime utcNow)
    {
        if (Status != ErrandStatus.Disputed)
            throw new InvalidErrandStateException(Status, nameof(ResolveDisputeAndClose));

        if (errandConsideredComplete)
        {
            Status = ErrandStatus.Completed;
            CompletedAt = utcNow;
        }
        else
        {
            Status = ErrandStatus.Cancelled;
            CancelledAt = utcNow;
        }
    }

    public void Cancel(DateTime utcNow)
    {
        if (Status is ErrandStatus.Completed or ErrandStatus.Cancelled or ErrandStatus.Disputed)
            throw new InvalidErrandStateException(Status, nameof(Cancel));

        Status = ErrandStatus.Cancelled;
        CancelledAt = utcNow;
    }

    public Message AddMessage(Guid senderId, string content, DateTime utcNow)
    {
        if (Status is ErrandStatus.Cancelled)
            throw new InvalidErrandStateException(Status, nameof(AddMessage));

        if (senderId != CustomerId && senderId != RunnerId)
            throw new ForbiddenDomainException(
    "Only the customer or assigned runner may message on this errand.");

        var message = Message.Create(Id, senderId, content, utcNow);
        _messages.Add(message);
        return message;
    }

    private void EnsureStatus(ErrandStatus required, string action)
    {
        if (Status != required)
            throw new InvalidErrandStateException(Status, action);
    }

    private void EnsureRunnerOwnership(Guid runnerId)
    {
        if (RunnerId != runnerId)
            throw new ForbiddenDomainException(
     "Only the assigned runner may perform this action.");
    }
}
