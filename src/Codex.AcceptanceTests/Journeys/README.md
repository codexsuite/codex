# Journeys

Cross-lane end-to-end journeys go here, in namespace `Codex.AcceptanceTests.Journeys`, one class per journey
named after the journey map (e.g. `J4_...`), tagged `[Category("J4")]`, one `[Test, Order(n)]` per step.
Run all: `dotnet test src/Codex.AcceptanceTests --filter "FullyQualifiedName~Codex.AcceptanceTests.Journeys."`
Run one: `--filter "Category=J4"`. See `docs/testing.md`.

J12 (Get ready to print) is `J12_GetReadyToPrint` (risks worked down through J10's paths, bibliography, APA, exports,
Write's Preview pane, Layout, Export for printer) plus `J12_GetReadyToPrint_RisksOpen` (the blocked case). Its preview,
Layout and Export steps need pandoc, Tectonic and codex-sandbox, as CI installs them; they fail on hosts where the
sandbox cannot start.

Journeys J15–J27 (wave 5) are written out in [docs/journeys-wave5.md](../../../docs/journeys-wave5.md).

Rules that cut across journeys (what happens to links, markers and notes when their target is deleted, archived or
moved; two tabs; a book with no chapters) are in [`design/README.md`](../../../design/README.md), "Cross-cutting
rules". Their tests are `CrossCuttingRules*.cs` here (`--filter "Category=CrossCuttingRules"`) and
`src/Codex.WebApi.Tests/CrossCuttingRules/`. A journey that exercises one of those operations asserts the rule; it
does not restate it.
