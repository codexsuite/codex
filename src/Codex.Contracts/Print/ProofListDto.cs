namespace Codex.Contracts.Print;

// GET /api/books/{id}/proofs?chapterId=: the chapter's proofs, newest first, and the cache key a proof of the chapter
// as it is now would have: a newest proof with another key is out of date.
public sealed record ProofListDto(string CurrentCacheKey, IReadOnlyList<ProofDto> Proofs);
