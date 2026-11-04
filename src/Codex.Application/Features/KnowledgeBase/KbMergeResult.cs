using Codex.Contracts.KnowledgeBase;

namespace Codex.Application.Features.KnowledgeBase;

// A merge's target as it reads now, and the snapshot that puts the merge back.
public sealed record KbMergeResult(KbEntryDto Target, KbMergeSnapshot Snapshot);
