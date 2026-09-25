using FluentValidation;
namespace Errandy.Application.Features.Runners.Commands.SubmitKyc;

public class SubmitKycCommandValidator : AbstractValidator<SubmitKycCommand>
{
    private static readonly string[] AllowedDocumentTypes =
        { "NIN", "Passport", "DriverLicense", "VotersCard" };
    public SubmitKycCommandValidator()
    {
        RuleFor(x => x.DocumentType)
            .NotEmpty().WithMessage("Document type is required.")
            .Must(t => AllowedDocumentTypes.Contains(t))
            .WithMessage($"Document type must be one of: {string.Join(", ", AllowedDocumentTypes)}.");
        RuleFor(x => x.DocumentNumber)
            .NotEmpty().WithMessage("Document number is required.")
            .MinimumLength(4)
            .MaximumLength(50);
        RuleFor(x => x.DocumentImageUrl)
            .NotEmpty().WithMessage("Document image URL is required.")
            .Must(BeAValidUrl).WithMessage("Document image URL must be a valid URL.");
    }
    private static bool BeAValidUrl(string url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var uri)
        && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps);
}
