using Errandy.Application.Common.Utils;
using FluentValidation;

namespace Errandy.Application.Features.Errands.UploadProof;

public class UploadProofCommandValidator : AbstractValidator<UploadProofCommand>
{
    public UploadProofCommandValidator()
    {
        RuleFor(x => x.ErrandId).NotEmpty();
        RuleFor(x => x.RunnerId).NotEmpty();

        RuleFor(x => x.FinalCost)
            .GreaterThan(0).WithMessage("FinalCost must be greater than zero.");

        RuleFor(x => x.ImageContent)
            .Cascade(CascadeMode.Stop)
            .Must(s => s.CanRead && s.CanSeek && s.Length > 0).WithMessage("A proof photo is required.")
            .Must(s => s.Length <= ProofFileRules.MaxBytes)
                .WithMessage($"The proof photo must be {ProofFileRules.MaxMegabytes} MB or smaller.")
            .Must(s => ImageFileInspector.Inspect(s) is not null)
                .WithMessage("The proof photo must be a JPEG, PNG or WebP image.");

        When(x => x.ReceiptContent is not null, () =>
        {
            RuleFor(x => x.ReceiptContent!)
                .Cascade(CascadeMode.Stop)
                .Must(s => s.CanRead && s.CanSeek && s.Length > 0).WithMessage("The receipt file is empty.")
                .Must(s => s.Length <= ProofFileRules.MaxBytes)
                    .WithMessage($"The receipt must be {ProofFileRules.MaxMegabytes} MB or smaller.")
                                .Must(s => ImageFileInspector.InspectReceipt(s) is not null)
                    .WithMessage("The receipt must be a JPEG, PNG or WebP image, or a PDF.");
        });
    }
}