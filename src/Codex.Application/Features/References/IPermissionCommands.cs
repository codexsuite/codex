using Codex.Contracts.References;
using Codex.Contracts.Storage;

namespace Codex.Application.Features.References;

// The signed letter as uploaded. Content is read once.
public sealed record PermissionLetterUpload(string FileName, string ContentType, long Length, Stream Content);

// The permission tracker. Every write answers with the refreshed source panel. Unknown source or request: 404. A
// transition the request's status does not allow: 409 conflict.
public interface IPermissionCommands
{
    // A draft letter from the source's uses and the book's letter settings. 409 while a draft, sent or granted request exists.
    Task<SourceDetailDto> DraftAsync(Guid sourceId, CancellationToken cancellationToken, EventProvenance? provenance = null);

    // Body and To edit a draft; RemindOn a draft or sent request. 400 for a blank letter or a bad date.
    Task<SourceDetailDto> PatchAsync(Guid requestId, PatchPermissionRequest request, CancellationToken cancellationToken);

    // Draft -> sent; stamps sent_at and, unless one is set, a reminder two weeks out. 400 for a blank letter.
    Task<SourceDetailDto> MarkSentAsync(Guid requestId, CancellationToken cancellationToken, EventProvenance? provenance = null);

    // Sent -> granted with the signed letter kept in the blob store. 400 without a file, an empty or oversize file, or a type not allowed.
    // `feeAmount` is optional; a negative one is 400.
    Task<SourceDetailDto> GrantAsync(Guid requestId, PermissionLetterUpload? letter, string? creditLine, string? terms, CancellationToken cancellationToken, decimal? feeAmount = null);

    // Sent request: stamps last_reminded_at and, if the reminder was due, moves it a week on. 409 unless sent.
    Task<SourceDetailDto> MarkFollowedUpAsync(Guid requestId, CancellationToken cancellationToken);

    // A granted request: drops the kept letter (the file too). 404 when it has none. 409 unless granted.
    Task<SourceDetailDto> RemoveLetterAsync(Guid requestId, CancellationToken cancellationToken);

    // A granted request with no letter kept: attaches one. Same file checks as the grant. 409 unless granted or when a letter is kept.
    Task<SourceDetailDto> AttachLetterAsync(Guid requestId, PermissionLetterUpload? letter, CancellationToken cancellationToken);

    // Sent -> refused; the risk stays and a new request can be drafted.
    Task<SourceDetailDto> RefuseAsync(Guid requestId, RefusePermissionRequest request, CancellationToken cancellationToken, EventProvenance? provenance = null);

    // Draft or sent -> withdrawn: the source has no request again.
    Task<SourceDetailDto> WithdrawAsync(Guid requestId, CancellationToken cancellationToken, EventProvenance? provenance = null);

    // The kept letter, or null when the request has none. Unknown request: 404.
    Task<(BlobContent Content, string FileName)?> GetLetterAsync(Guid requestId, CancellationToken cancellationToken);
}
