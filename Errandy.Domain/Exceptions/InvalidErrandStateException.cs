using Errandy.Domain.Enums;

namespace Errandy.Domain.Exceptions;

/// <summary>
/// Thrown when an operation is attempted against an Errand while it is in a
/// status that does not allow it (e.g. trying to Start an Errand that was
/// never Accepted). Caught centrally in the API layer and mapped to HTTP 409.
/// </summary>
public class InvalidErrandStateException : Exception
{
    public ErrandStatus CurrentStatus { get; }
    public string AttemptedAction { get; }

    public InvalidErrandStateException(ErrandStatus currentStatus, string attemptedAction)
        : base($"Cannot perform '{attemptedAction}' while Errand is in status '{currentStatus}'.")
    {
        CurrentStatus = currentStatus;
        AttemptedAction = attemptedAction;
    }
}
