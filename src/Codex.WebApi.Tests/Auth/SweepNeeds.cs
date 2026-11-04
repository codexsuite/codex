namespace Codex.WebApi.Tests.Auth;

// The least a route needs (#80): a book role (BookRole's order), or Own for things that belong to a user rather than a
// book (imports, layout templates, tokens, AI integrations), which only that user reaches; anyone else gets 404.
public enum SweepNeeds
{
    View,
    Comment,
    Write,
    Owner,
    Own,
}
