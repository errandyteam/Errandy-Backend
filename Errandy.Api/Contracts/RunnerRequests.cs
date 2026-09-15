namespace Errandy.Api.Contracts;

public record SubmitKycRequest(
    string DocumentType,
    string DocumentNumber,
    string DocumentImageUrl);
public record ReviewKycRequest(bool Approve, string? RejectionReason);