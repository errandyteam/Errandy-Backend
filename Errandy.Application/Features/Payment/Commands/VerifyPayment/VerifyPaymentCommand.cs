using System.Threading;
using System.Threading.Tasks;
using Errandy.Application.Common.DTOs;
using Errandy.Application.Common.Interfaces;
using Errandy.Application.Common.Results;
using FluentValidation;
using MediatR;

namespace Errandy.Application.Features.Payments.Commands.VerifyPayment;

public record VerifyPaymentCommand(string Reference) : IRequest<Result<PaymentVerificationDto>>;

public class VerifyPaymentCommandValidator : AbstractValidator<VerifyPaymentCommand>
{
    public VerifyPaymentCommandValidator()
    {
        RuleFor(x => x.Reference).NotEmpty().MaximumLength(100);
    }
}

public class VerifyPaymentCommandHandler
    : IRequestHandler<VerifyPaymentCommand, Result<PaymentVerificationDto>>
{
    private readonly ICurrentUser _currentUser;
    private readonly IPaymentVerificationService _verificationService;

    public VerifyPaymentCommandHandler(
        ICurrentUser currentUser,
        IPaymentVerificationService verificationService)
    {
        _currentUser = currentUser;
        _verificationService = verificationService;
    }

    public Task<Result<PaymentVerificationDto>> Handle(
        VerifyPaymentCommand request,
        CancellationToken cancellationToken)
    {
        if (!_currentUser.IsAuthenticated || _currentUser.UserId is null)
        {
            return Task.FromResult(Result<PaymentVerificationDto>.Failure(
                "User is not authenticated.", ErrorType.Unauthorized));
        }

        return _verificationService.VerifyOwnedPaymentAsync(
            request.Reference,
            _currentUser.UserId.Value,
            cancellationToken);
    }
}
