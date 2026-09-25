using FluentValidation;

namespace Errandy.Application.Features.Chat.SendMessage;

public class SendMessageCommandValidator : AbstractValidator<SendMessageCommand>
{
    public SendMessageCommandValidator()
    {
        RuleFor(x => x.ErrandId).NotEmpty();
        RuleFor(x => x.SenderId).NotEmpty();

        RuleFor(x => x.Content)
            .NotEmpty().WithMessage("Message content is required.")
            .MaximumLength(2000).WithMessage("Message content cannot exceed 2000 characters.");
    }
}
