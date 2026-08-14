using Errandy.Domain.Enums;
using FluentValidation;

namespace Errandy.Application.Features.Errands.CreateErrand;

public class CreateErrandCommandValidator : AbstractValidator<CreateErrandCommand>
{
    public CreateErrandCommandValidator()
    {
        RuleFor(x => x.CustomerId)
            .NotEmpty().WithMessage("CustomerId is required.");

        RuleFor(x => x.Category)
            .IsInEnum().WithMessage("A valid errand category is required.");

        RuleFor(x => x.Description)
            .NotEmpty().WithMessage("Description is required.")
            .MaximumLength(1000).WithMessage("Description cannot exceed 1000 characters.");

        RuleFor(x => x.EstimatedCost)
            .GreaterThan(0).WithMessage("EstimatedCost must be greater than zero.");

        RuleFor(x => x.PickupLatitude)
            .InclusiveBetween(-90, 90).WithMessage("PickupLatitude must be a valid latitude.");

        RuleFor(x => x.PickupLongitude)
            .InclusiveBetween(-180, 180).WithMessage("PickupLongitude must be a valid longitude.");

        RuleFor(x => x.Deadline)
            .GreaterThan(DateTime.UtcNow)
            .When(x => x.Deadline.HasValue)
            .WithMessage("Deadline must be in the future.");
    }
}
