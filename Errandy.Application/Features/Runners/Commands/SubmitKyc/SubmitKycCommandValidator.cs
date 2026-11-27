using FluentValidation;

namespace Errandy.Application.Features.Runners.Commands.SubmitKyc;

public class SubmitKycCommandValidator : AbstractValidator<SubmitKycCommand>
{
    public SubmitKycCommandValidator()
    {
        RuleFor(x => x.DocumentType)
            .NotEmpty();

        RuleFor(x => x.DocumentNumber)
            .NotEmpty();

        RuleFor(x => x.DocumentImageUrl)
            .NotEmpty();

        RuleFor(x => x.SelfieImageUrl)
            .NotEmpty();

        // Add these only if BVN/bank details are required before admin review:
        // RuleFor(x => x.Bvn).NotEmpty();
        // RuleFor(x => x.BankName).NotEmpty();
        // RuleFor(x => x.BankAccountNumber).NotEmpty();
        // RuleFor(x => x.BankAccountName).NotEmpty();
    }
}