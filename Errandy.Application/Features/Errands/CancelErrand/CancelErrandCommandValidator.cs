using FluentValidation;

namespace Errandy.Application.Features.Errands.CancelErrand;

public class CancelErrandCommandValidator : AbstractValidator<CancelErrandCommand>
{
    public CancelErrandCommandValidator()
    {
        RuleFor(x => x.ErrandId).NotEmpty();
        RuleFor(x => x.RequestingUserId).NotEmpty();
    }
}