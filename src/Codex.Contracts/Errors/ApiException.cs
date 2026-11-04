namespace Codex.Contracts.Errors;

// Thrown by commands and queries; the WebApi middleware turns it into an ApiError body with the code's
// HTTP status. Use the factories so the code and the status stay paired.
public sealed class ApiException(string code, string message) : Exception(message)
{
    public string Code { get; } = code;

    public int Status => ApiErrorCodes.StatusFor(Code);

    public ApiError ToApiError() => new(Message, Code);

    public static ApiException Internal() => new(ApiErrorCodes.Internal, "Something went wrong on the server.");

    public static ApiException NotFound(string message) => new(ApiErrorCodes.NotFound, message);

    public static ApiException Conflict(string message) => new(ApiErrorCodes.Conflict, message);

    public static ApiException Validation(string message) => new(ApiErrorCodes.Validation, message);

    public static ApiException Ambiguous(string message) => new(ApiErrorCodes.Ambiguous, message);

    public static ApiException OutOfScope(string message) => new(ApiErrorCodes.OutOfScope, message);

    public static ApiException Blocked(string message) => new(ApiErrorCodes.Blocked, message);

    public static ApiException NotConfigured(string message) => new(ApiErrorCodes.NotConfigured, message);

    public static ApiException NeedsConfirmation(string message) => new(ApiErrorCodes.NeedsConfirmation, message);

    public static ApiException Unauthorized(string message) => new(ApiErrorCodes.Unauthorized, message);

    public static ApiException Forbidden(string message) => new(ApiErrorCodes.Forbidden, message);

    public static ApiException RateLimited(string message) => new(ApiErrorCodes.RateLimited, message);
}
