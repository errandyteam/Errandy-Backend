using FluentValidation;

namespace Errandy.Application.Features.Users.Commands.UpdateProfile;

public class UpdateProfileCommandValidator : AbstractValidator<UpdateProfileCommand>
{
    public UpdateProfileCommandValidator()
    {
        RuleFor(x => x.FullName)
            .NotEmpty().WithMessage("Full name is required.")
            .MinimumLength(2)
            .MaximumLength(150);

        RuleFor(x => x.PhoneNumber)
            .NotEmpty().WithMessage("Phone number is required.")
            .Matches(@"^\+?[0-9]{7,15}$")
            .WithMessage("Phone number must be 7-15 digits and may start with '+'.");
    }
}