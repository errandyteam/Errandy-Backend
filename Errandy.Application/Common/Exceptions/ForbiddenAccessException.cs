namespace Errandy.Application.Common.Exceptions;

/// <summary>
/// Thrown when the current user is authenticated but not allowed to perform
/// the requested action (e.g. a customer trying to accept their own errand,
/// or a runner trying to view someone else's chat). Map this to HTTP 403 in
/// the API layer's global exception handling middleware.
/// </summary>
public class ForbiddenAccessException : Exception
{
    public ForbiddenAccessException(string message) : base(message) { }
}
