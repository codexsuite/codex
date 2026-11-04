namespace Codex.Contracts.References;

// A null part is left alone. Body and To edit a draft; RemindOn (yyyy-MM-dd, empty = no reminder) works on a draft or
// a sent request. RemindInDays (1 to 365) is the same reminder counted from today by the server's clock and wins over
// RemindOn. Anything else is 409 conflict.
public sealed record PatchPermissionRequest(string? Body, string? To, string? RemindOn, int? RemindInDays = null);
