using FluentValidation;

namespace Errandy.Application.Features.Errands.ProposePriceAdjustment;

public class ProposePriceAdjustmentCommandValidator : AbstractValidator<ProposePriceAdjustmentCommand>
{
    public ProposePriceAdjustmentCommandValidator()
    {
        RuleFor(x => x.ErrandId).NotEmpty();
        RuleFor(x => x.RunnerId).NotEmpty();
        RuleFor(x => x.NewCost).GreaterThan(0);
        RuleFor(x => x.Reason).NotEmpty().MaximumLength(500);
    }
}