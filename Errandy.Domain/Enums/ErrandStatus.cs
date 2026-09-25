namespace Errandy.Domain.Enums;

/// <summary>
/// Lifecycle of an Errand. Transitions are enforced inside the Errand entity itself
/// (see Errand.Accept / Start / UploadProof / Confirm / Cancel), never set directly
/// from the Application layer.
/// </summary>
public enum ErrandStatus
{
    Created = 0,
    Accepted = 1,
    InProgress = 2,
    PendingConfirmation = 3,
    Completed = 4,
    Disputed = 5,
    Cancelled = 6
}
