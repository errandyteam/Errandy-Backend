using FluentValidation;
namespace Errandy.Application.Features.Runners.Commands.ReviewKyc;

public class ReviewKycCommandValidator : AbstractValidator<ReviewKycCommand>
{
    public ReviewKycCommandValidator()
    {
        RuleFor(x => x.RunnerProfileId).NotEmpty();
        RuleFor(x => x.RejectionReason)
            .NotEmpty().WithMessage("A rejection reason is required when rejecting KYC.")
            .MaximumLength(500)
            .When(x => !x.Approve);
    }
}
