namespace Codex.Contracts;

// BookRole to and from its wire value, the lower-case name book_access.role and book_grant.role store.
public static class BookRoles
{
    public static string ToWire(this BookRole role) => role switch
    {
        BookRole.View => "view",
        BookRole.Comment => "comment",
        BookRole.Write => "write",
        BookRole.Owner => "owner",
        _ => throw new ArgumentOutOfRangeException(nameof(role), role, null),
    };

    public static BookRole Parse(string value) => TryParse(value)
        ?? throw new ArgumentOutOfRangeException(nameof(value), value, "Not a book role.");

    public static BookRole? TryParse(string? value) => value switch
    {
        "view" => BookRole.View,
        "comment" => BookRole.Comment,
        "write" => BookRole.Write,
        "owner" => BookRole.Owner,
        _ => null,
    };
}
