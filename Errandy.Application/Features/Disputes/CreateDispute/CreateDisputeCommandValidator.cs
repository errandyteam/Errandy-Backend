using FluentValidation;

namespace Errandy.Application.Features.Disputes.CreateDispute;

public class CreateDisputeCommandValidator : AbstractValidator<CreateDisputeCommand>
{
    public CreateDisputeCommandValidator()
    {
        RuleFor(x => x.ErrandId).NotEmpty();
        RuleFor(x => x.RaisedBy).NotEmpty();

        RuleFor(x => x.Reason).IsInEnum();

        RuleFor(x => x.Description)
            .MaximumLength(2000).WithMessage("Description cannot exceed 2000 characters.");

        RuleForEach(x => x.EvidenceUrls)
            .Must(url => Uri.TryCreate(url, UriKind.Absolute, out _))
            .WithMessage("Each evidence URL must be a valid absolute URL.");
    }
}
