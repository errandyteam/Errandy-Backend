using Errandy.Domain.Enums;
using FluentValidation;

namespace Errandy.Application.Features.Errands.CreateErrand;

public class CreateErrandCommandValidator : AbstractValidator<CreateErrandCommand>
{
    public CreateErrandCommandValidator()
    {
        RuleFor(x => x.CustomerId).NotEmpty();
        RuleFor(x => x.Category).IsInEnum();

        RuleFor(x => x.Description)
            .NotEmpty()
            .MaximumLength(1000);

        RuleFor(x => x.EstimatedCost)
            .GreaterThan(0);

        RuleFor(x => x.PickupLatitude).InclusiveBetween(-90, 90);
        RuleFor(x => x.PickupLongitude).InclusiveBetween(-180, 180);

        RuleFor(x => x.TimePreference).IsInEnum();

        RuleFor(x => x.ScheduledDeadline)
            .NotNull().WithMessage("ScheduledDeadline is required when TimePreference is Scheduled.")
            .GreaterThan(DateTime.UtcNow).WithMessage("ScheduledDeadline must be in the future.")
            .When(x => x.TimePreference == ErrandTimePreference.Scheduled);
    }
}