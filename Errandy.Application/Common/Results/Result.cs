namespace Errandy.Application.Common.Results;
/// <summary>
/// A lightweight result type used across the application layer to
/// communicate success/failure without throwing for expected flows.
/// </summary>
public class Result
{
    public bool IsSuccess { get; }
    public string? Error { get; }
    public ErrorType ErrorType { get; }
    protected Result(bool isSuccess, string? error, ErrorType errorType)
    {
        IsSuccess = isSuccess;
        Error = error;
        ErrorType = errorType;
    }
    public static Result Success() => new(true, null, ErrorType.None);
    public static Result Failure(string error, ErrorType type = ErrorType.Validation)
        => new(false, error, type);
}
public class Result<T> : Result
{
    public T? Value { get; }
    private Result(T? value, bool isSuccess, string? error, ErrorType errorType)
        : base(isSuccess, error, errorType) => Value = value;
    public static Result<T> Success(T value) => new(value, true, null, ErrorType.None);
    public static new Result<T> Failure(string error, ErrorType type = ErrorType.Validation)
        => new(default, false, error, type);
}
public enum ErrorType
{
    None = 0,
    Validation = 1,
    NotFound = 2,
    Conflict = 3,
    Unauthorized = 4,
    Forbidden = 5
}