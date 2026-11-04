namespace Codex.Contracts.KnowledgeBase;

// A field the author added to one entry ("URL": "https://..."), shown in the rail after the category's Details.
public sealed record KbCustomFieldDto(string Label, string Value);
