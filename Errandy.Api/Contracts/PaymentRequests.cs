namespace Errandy.Api.Contracts;

// Amount is in naira; the application converts it to kobo for Paystack.
public record InitializeWalletPaymentRequest(decimal Amount);

public record VerifyWalletPaymentRequest(string Reference);
public record AdminReconcilePaymentRequest(string Reference, string Reason);
