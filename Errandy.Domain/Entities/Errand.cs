using Errandy.Domain.Enums;
using Errandy.Domain.Exceptions;


namespace Errandy.Domain.Entities;

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
    public ErrandTimePreference TimePreference { get; private set; }

    public DateTime CreatedAt { get; private set; }
    public DateTime? AcceptedAt { get; private set; }
    public DateTime? StartedAt { get; private set; }
    public DateTime? PendingConfirmationAt { get; private set; }
    public DateTime? CompletedAt { get; private set; }
    public DateTime? CancelledAt { get; private set; }

    public decimal? ProposedCost { get; private set; }
    public string? ProposedCostReason { get; private set; }
    public DateTime? ProposedCostAt { get; private set; }

    private readonly List<Message> _messages = new();
    public IReadOnlyCollection<Message> Messages => _messages.AsReadOnly();

    public Proof? Proof { get; private set; }
    public Dispute? Dispute { get; private set; }

    private Errand() { }

    public static Errand Create(
        Guid customerId,
        ErrandCategory category,
        string description,
        decimal estimatedCost,
        double pickupLatitude,
        double pickupLongitude,
        ErrandTimePreference timePreference,
        DateTime? scheduledDeadline,
        DateTime utcNow)
    {
        if (customerId == Guid.Empty)
            throw new ArgumentException("CustomerId is required.", nameof(customerId));

        if (string.IsNullOrWhiteSpace(description))
            throw new ArgumentException("Description is required.", nameof(description));

        if (estimatedCost <= 0)
            throw new ArgumentException("EstimatedCost must be greater than zero.", nameof(estimatedCost));

        if (timePreference == ErrandTimePreference.Scheduled && scheduledDeadline is null)
            throw new ArgumentException("A scheduledDeadline is required when TimePreference is Scheduled.", nameof(scheduledDeadline));

        if (timePreference == ErrandTimePreference.Scheduled && scheduledDeadline <= utcNow)
            throw new ArgumentException("scheduledDeadline must be in the future.", nameof(scheduledDeadline));

        var computedDeadline = timePreference switch
        {
            ErrandTimePreference.Asap => utcNow.AddHours(2),
            ErrandTimePreference.SameDay => utcNow.Date.AddDays(1).AddTicks(-1),
            ErrandTimePreference.Scheduled => scheduledDeadline!.Value,
            _ => throw new ArgumentException($"Unsupported TimePreference: {timePreference}")
        };

        return new Errand
        {
            Id = Guid.NewGuid(),
            CustomerId = customerId,
            Category = category,
            Description = description.Trim(),
            EstimatedCost = estimatedCost,
            PickupLatitude = pickupLatitude,
            PickupLongitude = pickupLongitude,
            Deadline = computedDeadline,
            TimePreference = timePreference,
            Status = ErrandStatus.Created,
            CreatedAt = utcNow
        };
    }

    public bool IsOverdue(DateTime utcNow)
    {
        if (Deadline is null)
            return false;

        var terminal = Status is ErrandStatus.Completed or ErrandStatus.Cancelled or ErrandStatus.Disputed;
        return !terminal && utcNow > Deadline.Value;
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

    public void UnassignRunner(DateTime utcNow)
    {
        EnsureStatus(ErrandStatus.Accepted, nameof(UnassignRunner));
        RunnerId = null;
        AcceptedAt = null;
        Status = ErrandStatus.Created;
    }

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

    public void ConfirmCompletion(DateTime utcNow)
    {
        EnsureStatus(ErrandStatus.PendingConfirmation, nameof(ConfirmCompletion));
        Status = ErrandStatus.Completed;
        CompletedAt = utcNow;
    }

    public void AdminForceComplete(DateTime utcNow)
    {
        if (Status is not (ErrandStatus.Accepted or ErrandStatus.InProgress or ErrandStatus.PendingConfirmation))
            throw new InvalidErrandStateException(Status, nameof(AdminForceComplete));

        FinalCost ??= EstimatedCost;
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

    public void ProposePriceAdjustment(Guid runnerId, decimal newCost, string reason, DateTime utcNow)
    {
        EnsureStatus(ErrandStatus.InProgress, nameof(ProposePriceAdjustment));
        EnsureRunnerOwnership(runnerId);

        if (newCost <= 0)
            throw new ArgumentException("Proposed cost must be greater than zero.", nameof(newCost));

        if (ProposedCost is not null)
            throw new InvalidOperationException("A price adjustment is already pending for this errand.");

        ProposedCost = newCost;
        ProposedCostReason = reason;
        ProposedCostAt = utcNow;
    }

    public void ApprovePriceAdjustment(DateTime utcNow)
    {
        if (ProposedCost is null)
            throw new InvalidOperationException("There is no pending price adjustment to approve.");

        EstimatedCost = ProposedCost.Value;
        ClearPendingProposal();
    }

    public void RejectPriceAdjustment(DateTime utcNow)
    {
        if (ProposedCost is null)
            throw new InvalidOperationException("There is no pending price adjustment to reject.");

        ClearPendingProposal();
    }

    private void ClearPendingProposal()
    {
        ProposedCost = null;
        ProposedCostReason = null;
        ProposedCostAt = null;
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