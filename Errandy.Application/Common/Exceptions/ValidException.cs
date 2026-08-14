using FluentValidation.Results;
namespace Errandy.Application.Common.Exceptions;
/// <summary>
/// Thrown by the validation pipeline behavior when a request fails one or more
/// FluentValidation rules. Translated to a 400 response by the API middleware.
/// </summary>
public class ValidationException : Exception
{
    public IReadOnlyDictionary<string, string[]> Errors { get; }
    public ValidationException()
        : base("One or more validation failures have occurred.")
    {
        Errors = new Dictionary<string, string[]>();
    }
    public ValidationException(IEnumerable<ValidationFailure> failures) : this()
    {
        Errors = failures
            .GroupBy(f => f.PropertyName, f => f.ErrorMessage)
            .ToDictionary(g => g.Key, g => g.ToArray());
    }
}