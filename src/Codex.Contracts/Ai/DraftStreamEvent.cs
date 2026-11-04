using Codex.Contracts.Errors;

namespace Codex.Contracts.Ai;

// One line of the generate stream (application/x-ndjson, one JSON object per line, null members omitted). Exactly one
// member is set per line, in this order: `started` once, `text` any number of times, then `final` (completed) or
// `error` (the generator failed after the stream began; the row is failed). A stopped stream (client abort) ends
// without a closing line and the server keeps the text received so far as a pending draft. A failure before the
// first chunk is an ordinary HTTP error (not_configured is 503), not a stream line.
public sealed record DraftStreamEvent(DraftStreamStarted? Started = null, string? Text = null, DraftStreamFinal? Final = null, ApiError? Error = null);
