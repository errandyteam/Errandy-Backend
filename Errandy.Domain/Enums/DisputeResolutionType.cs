namespace Errandy.Domain.Enums;

/// <summary>
/// The outcome an Admin chooses when resolving a Dispute (PRD 12.3).
/// This only records the *decision*; actually moving money is Engineer B's
/// IEscrowService responsibility, invoked by the ResolveDisputeCommandHandler.
/// </summary>
public enum DisputeResolutionType
{
    None = 0,
    FullPaymentToRunner = 1,
    PartialRefundToCustomer = 2,
    FullRefundToCustomer = 3,
    PenaltyToRunner = 4
}
