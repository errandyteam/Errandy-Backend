namespace Errandy.Domain.Exceptions;

public class ForbiddenDomainException : Exception
{
    public ForbiddenDomainException(string message)
        : base(message)
    {
    }
}