using FluentValidation;

namespace Errandy.Application.Features.Disputes.AddDisputeEvidence;

public class AddDisputeEvidenceCommandValidator : AbstractValidator<AddDisputeEvidenceCommand>
{
    public AddDisputeEvidenceCommandValidator()
    {
        RuleFor(x => x.DisputeId).NotEmpty();
        RuleFor(x => x.UserId).NotEmpty();

        RuleFor(x => x.EvidenceUrls)
            .NotEmpty().WithMessage("At least one evidence link is required.")
            .Must(urls => urls.Count <= 10).WithMessage("You can add at most 10 evidence links at a time.");

        RuleForEach(x => x.EvidenceUrls)
            .Must(BeAWebUrl).WithMessage("Each evidence link must be a valid http or https URL.");
    }

    private static bool BeAWebUrl(string? url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var uri)
        && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps);
}