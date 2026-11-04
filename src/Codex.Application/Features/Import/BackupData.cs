using System.Text.Json.Nodes;

namespace Codex.Application.Features.Import;

// What a Codex backup holds, read from the zip (CodexBackupParser) for BackupWriter. Book, Structure, KnowledgeBase,
// References and Labels are the JSON files; Bodies maps a scene's `file` to its prose as stored. Words, Sources,
// CitedSources and LabelCount are for the preview and the banner.
public sealed record BackupData(
    JsonObject Book,
    JsonObject Structure,
    JsonObject KnowledgeBase,
    JsonObject References,
    JsonObject Labels,
    IReadOnlyDictionary<string, string> Bodies,
    int Words,
    int Sources,
    int CitedSources,
    int LabelCount);
