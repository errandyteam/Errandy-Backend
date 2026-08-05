using FluentValidation;

namespace Errandy.Application.Features.Errands.UploadProof;

public class UploadProofCommandValidator : AbstractValidator<UploadProofCommand>
{
    public UploadProofCommandValidator()
    {
        RuleFor(x => x.ErrandId).NotEmpty();
        RuleFor(x => x.RunnerId).NotEmpty();

        RuleFor(x => x.ImageUrl)
            .NotEmpty().WithMessage("A proof image is required.")
            .Must(BeAValidUrl).WithMessage("ImageUrl must be a valid URL.");

        RuleFor(x => x.ReceiptUrl)
            .Must(BeAValidUrl)
            .When(x => !string.IsNullOrWhiteSpace(x.ReceiptUrl))
            .WithMessage("ReceiptUrl must be a valid URL.");

        RuleFor(x => x.FinalCost)
            .GreaterThan(0).WithMessage("FinalCost must be greater than zero.");
    }

    private static bool BeAValidUrl(string? url)
    {
        return !string.IsNullOrWhiteSpace(url) && Uri.TryCreate(url, UriKind.Absolute, out _);
    }
}
