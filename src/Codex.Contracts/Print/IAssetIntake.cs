using System.Data.Common;

namespace Codex.Contracts.Print;

// The checks and storage of an image upload (PNG, JPEG or WebP; 25 MB; 64 megapixels; the original in the blob store;
// the rendition job), split around a caller's own transaction: for a writer that creates a book and its images
// together (the import), where the book does not exist until that transaction commits. IAssetService.UploadAsync is
// the same steps in a transaction of its own. No ownership check: the caller owns the book it is creating.
public interface IAssetIntake
{
    // Reads (at most one byte past the cap), checks and probes the image, and stores the original under the book's key.
    // Writes no row. 400 validation, with the upload's message, for anything IAssetService.UploadAsync refuses.
    // Source, prompt and licence are the optional notes kept in asset.meta.
    Task<StagedAsset> StageAsync(Guid bookId, StagedAssetFile file, CancellationToken cancellationToken);

    // Inserts the asset row (owned by the current user) and queues its renditions, in the caller's transaction.
    Task InsertAsync(DbConnection connection, DbTransaction transaction, StagedAsset asset, CancellationToken cancellationToken);

    // Deletes a staged original whose transaction did not commit.
    Task DiscardAsync(StagedAsset asset, CancellationToken cancellationToken);

    // Wakes the rendition worker once the rows are committed.
    void Notify();
}
