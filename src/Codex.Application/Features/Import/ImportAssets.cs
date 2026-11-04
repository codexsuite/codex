using Codex.Contracts.Print;

namespace Codex.Application.Features.Import;

// The new book's id, chosen before its images are stored under it, and those images by their path in the upload.
public sealed record ImportAssets(Guid BookId, IReadOnlyDictionary<string, StagedAsset> ByPath);
