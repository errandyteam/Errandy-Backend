namespace Errandy.Api.Contracts;

public record SubmitKycRequest(
    string DocumentType,
    string DocumentNumber,
    string DocumentImageUrl,
    string SelfieImageUrl);

//Incase BVN is needed
//    string? Bvn = null,
//    string? BankName = null,
//    string? BankAccountNumber = null,
//    string? BankAccountName = null;
public record ReviewKycRequest(bool Approve, string? RejectionReason);