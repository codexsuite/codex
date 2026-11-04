namespace Codex.Contracts.KnowledgeBase;

// A Details field a category's entries show: values live in the entry's fields, keyed by Key.
public sealed record KbFieldDefDto(string Key, string Label);
