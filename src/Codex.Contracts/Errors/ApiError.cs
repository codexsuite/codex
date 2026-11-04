namespace Codex.Contracts.Errors;

// The one error body every endpoint returns, MVC or minimal API: { "error": "...", "code": "..." }.
// `error` is a sentence the UI can show; `code` is one of ApiErrorCodes (#20 section 2.3 plus `validation`).
public sealed record ApiError(string Error, string Code);
