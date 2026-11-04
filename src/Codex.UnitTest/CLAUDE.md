# Codex.UnitTest

Use this project sparingly. It is the exception, not the default.

Most tests belong elsewhere, in this order of preference:

1. **`Codex.AcceptanceTests`**: Playwright against the real BFF and the built SPA bundle. Best for anything a user can see or do.
2. **`Codex.WebApi.Tests`**: black-box tests through `WebApplicationFactory` and the HTTP surface. Best for API behaviour, status codes, persistence and auth.
3. **`Codex.UnitTest`** (here): last resort.

## What belongs here

Only pure logic that is awkward to reach through the layers above: helper methods, formatters, parsers, calculations with many edge cases (`Codex.Core`, and pure classes in `Codex.Application` such as the prose helpers under `Prose/`). No database, no DI container, no HTTP. Reading repository files through `RepoPaths` (the prose corpus, fixtures) is fine.

Two other kinds of test live here because they need no host:

- **Contract tests** (`Contracts/`): an interface's contract test base class lives in `Codex.TestSupport/Contracts/`; each real implementation and each double gets a fixture here that inherits it.
- **Architecture tests** (`Architecture/`): reflection over the production assemblies (feature isolation, no `Codex.TestSupport` or EF Core references).

That is why this project references `Codex.Core`, `Codex.Contracts`, `Codex.Application`, `Codex.WebApi` and `Codex.TestSupport`. Anything that needs a database or the running app goes in `Codex.WebApi.Tests`.

## What does not belong here

- Controllers, services, middleware or anything wired through DI: test it through the HTTP surface in `Codex.WebApi.Tests`.
- Mocks of our own types. If a test needs one, it is at the wrong level.
- A test per method for coverage's sake. If an acceptance or API test already pins the behaviour, do not duplicate it here.

Naming is `Method_When_Should`, as everywhere (`.claude/skills/csharp-testing-strategy/`). Never weaken or delete an existing test (`.claude/skills/preserve-existing-tests/`).
