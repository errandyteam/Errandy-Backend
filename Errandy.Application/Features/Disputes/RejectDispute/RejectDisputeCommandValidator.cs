using FluentValidation;

namespace Errandy.Application.Features.Disputes.RejectDispute;

public class RejectDisputeCommandValidator : AbstractValidator<RejectDisputeCommand>
{
    public RejectDisputeCommandValidator()
    {
        RuleFor(x => x.DisputeId).NotEmpty();
        RuleFor(x => x.AdminId).NotEmpty();
        RuleFor(x => x.Notes)
            .NotEmpty().WithMessage("A reason is required when rejecting a dispute.")
            .MaximumLength(2000);
    }
}