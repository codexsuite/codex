namespace Codex.Contracts.KnowledgeBase;

public sealed record KbDto(IReadOnlyList<KbCategoryDto> Categories, IReadOnlyList<KbEntrySummaryDto> Entries);
