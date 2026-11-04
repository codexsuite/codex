Large-book seed: turns *Iron Litany* into a ~125,000-word, three-act, 30-chapter, 90-scene manuscript (issue #16 review, section C item 7).

Applied only by `CodexDatabase.CloneSeedLargeAsync()` (template `codex_seedlarge_<hash>`), after `db/seed`, for performance tests.
It is not embedded in `Codex.Application`, so the app, `scripts/db-reset.sh` and the normal seed template never run it, and
`SeedLargeIsolationTests` proves the normal seed still has Iron Litany at 855 words. Deterministic: no `random()`, no clock.
