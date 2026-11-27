using Errandy.Application.Common.DTOs;
using Errandy.Application.Common.Interfaces;
using Errandy.Application.Common.Results;
using Errandy.Domain.Enums;
using MediatR;

namespace Errandy.Application.Features.Runners.Commands.SubmitKyc;

public class SubmitKycCommandHandler
    : IRequestHandler<SubmitKycCommand, Result<RunnerProfileDto>>
{
    private readonly IRunnerProfileRepository _profiles;
    private readonly IUnitOfWork _uow;
    private readonly IKycVerificationService _kyc;

    public SubmitKycCommandHandler(
        IRunnerProfileRepository profiles,
        IUnitOfWork uow,
        IKycVerificationService kyc)
    {
        _profiles = profiles;
        _uow = uow;
        _kyc = kyc;
    }

    public async Task<Result<RunnerProfileDto>> Handle(
        SubmitKycCommand request,
        CancellationToken cancellationToken)
    {
        var profile = await _profiles.GetByUserIdAsync(request.UserId, cancellationToken);

        if (profile is null)
        {
            return Result<RunnerProfileDto>.Failure(
                "Runner profile not found. Become a runner first.",
                ErrorType.NotFound);
        }

        if (profile.KycStatus == KycStatus.Approved)
        {
            return Result<RunnerProfileDto>.Failure(
                "KYC has already been approved.",
                ErrorType.Conflict);
        }

        if (profile.KycStatus == KycStatus.Pending)
        {
            return Result<RunnerProfileDto>.Failure(
                "KYC has already been submitted and is awaiting admin review.",
                ErrorType.Conflict);
        }

        // NotSubmitted or Rejected: the runner may submit or resubmit.

        var documentType = request.DocumentType?.Trim().ToUpperInvariant();
        var documentNumber = request.DocumentNumber?.Trim();
        var documentImageUrl = request.DocumentImageUrl?.Trim();
        var selfieImageUrl = request.SelfieImageUrl?.Trim();
        var bvn = request.Bvn?.Trim();
        var bankName = request.BankName?.Trim();
        var bankAccountNumber = request.BankAccountNumber?.Trim();
        var bankAccountName = request.BankAccountName?.Trim();

        var missingFields = GetMissingRequiredFields(
            documentType,
            documentNumber,
            documentImageUrl,
            selfieImageUrl,
            bvn,
            bankName,
            bankAccountNumber,
            bankAccountName);

        if (missingFields.Count > 0)
        {
            return Result<RunnerProfileDto>.Failure(
                $"Please submit all required KYC documents: {string.Join(", ", missingFields)}.",
                ErrorType.Validation);
        }

        // Admins open these links in a browser, so only accept https.
        if (!IsHttpsUrl(documentImageUrl) || !IsHttpsUrl(selfieImageUrl))
        {
            return Result<RunnerProfileDto>.Failure(
                "Document and selfie images must be valid https links.",
                ErrorType.Validation);
        }

        // Legacy columns and the newer GovernmentId columns hold the same values for now.
        profile.DocumentType = documentType!;
        profile.DocumentNumber = documentNumber!;
        profile.DocumentImageUrl = documentImageUrl!;

        profile.GovernmentIdType = documentType!;
        profile.GovernmentIdNumber = documentNumber!;
        profile.GovernmentIdImageUrl = documentImageUrl!;

        profile.SelfieImageUrl = selfieImageUrl!;
        profile.Bvn = bvn;
        profile.BankName = bankName;
        profile.BankAccountNumber = bankAccountNumber;
        profile.BankAccountName = bankAccountName;

        // Fresh submission: clear any previous review and reset verification statuses.
        profile.RejectionReason = null;
        profile.ReviewedAt = null;
        profile.GovernmentIdVerificationStatus = VerificationStatus.Pending;
        profile.SelfieVerificationStatus = VerificationStatus.Pending;
        profile.BankVerificationStatus = VerificationStatus.Pending;
        profile.UpdatedAt = DateTime.UtcNow;

        var verification = await _kyc.VerifyAsync(profile, cancellationToken);

        if (!verification.Success)
        {
            var failureMessage = verification.RejectionReason ?? "Identity verification failed.";

            profile.KycStatus = KycStatus.Rejected;
            profile.RejectionReason = failureMessage;
            profile.GovernmentIdVerificationStatus = VerificationStatus.Rejected;
            profile.UpdatedAt = DateTime.UtcNow;

            _profiles.Update(profile);
            await _uow.SaveChangesAsync(cancellationToken);

            return Result<RunnerProfileDto>.Failure(failureMessage, ErrorType.Validation);
        }

        // The check passed (or Manual mode is on). Only an admin can approve,
        // and the admin review sets the ID status to Verified.
        profile.KycStatus = KycStatus.Pending;
        profile.RejectionReason = null;
        profile.UpdatedAt = DateTime.UtcNow;

        _profiles.Update(profile);
        await _uow.SaveChangesAsync(cancellationToken);

        return Result<RunnerProfileDto>.Success(RunnerProfileDto.FromEntity(profile));
    }

    private static List<string> GetMissingRequiredFields(
        string? documentType,
        string? documentNumber,
        string? documentImageUrl,
        string? selfieImageUrl,
        string? bvn,
        string? bankName,
        string? bankAccountNumber,
        string? bankAccountName)
    {
        var missing = new List<string>();

        if (IsMissing(documentType)) missing.Add("document type");
        if (IsMissing(documentNumber)) missing.Add("document number");
        if (IsMissing(documentImageUrl)) missing.Add("document image");
        if (IsMissing(selfieImageUrl)) missing.Add("selfie image");

        // Uncomment once the team decides bank/BVN is required before admin review.
        // if (IsMissing(bvn)) missing.Add("BVN");
        // if (IsMissing(bankName)) missing.Add("bank name");
        // if (IsMissing(bankAccountNumber)) missing.Add("bank account number");
        // if (IsMissing(bankAccountName)) missing.Add("bank account name");

        return missing;
    }

    private static bool IsMissing(string? value) => string.IsNullOrWhiteSpace(value);

    private static bool IsHttpsUrl(string? value) =>
        Uri.TryCreate(value, UriKind.Absolute, out var uri)
        && uri.Scheme == Uri.UriSchemeHttps;
}