# Testing

Run `source ~/.local/codex-env.sh` first on the shared agent host (.NET on PATH, `CODEX_TEST_DB` pointing at the
long-lived local Postgres). Without `CODEX_TEST_DB`, tests start a `postgres:18` Testcontainers container (CI).

## Tiers and commands

| Tier | Where | Command | Warm time |
|---|---|---|---|
| Build | `src/Codex.slnx` | `dotnet build src/Codex.slnx` | ~5 s |
| Unit | `src/Codex.UnitTest` (no DB, no host) | `dotnet test src/Codex.UnitTest` | ~3 s |
| API, all | `src/Codex.WebApi.Tests` | `dotnet test src/Codex.WebApi.Tests` | ~6 s |
| API, one class | | `dotnet test src/Codex.WebApi.Tests --no-build --filter FullyQualifiedName~<Class>` | 2-6 s |
| Vitest | `src/Codex.Client` | `pnpm test` | ~2 s |
| Component (CT) | `src/Codex.Client` | `pnpm run test-ct` | ~2 s + per test |
| Lint / types | `src/Codex.Client` | `pnpm run lint`, `pnpm run typecheck` | ~2 s / ~1 s |
| Client build | `src/Codex.Client` | `pnpm build` (emits `src/Codex.WebApi/wwwroot`, gitignored) | ~2 s |
| Acceptance, one test | `src/Codex.AcceptanceTests` | `dotnet test src/Codex.AcceptanceTests --filter FullyQualifiedName~<Class>` (or `~<Class>.<Test>`) | ~9 s |
| Acceptance, one lane | | `--filter "FullyQualifiedName~Codex.AcceptanceTests.Lanes.Plan."` | lane size |
| Acceptance, harness | | `--filter "FullyQualifiedName~Codex.AcceptanceTests.Harness."` | ~10 s |
| Acceptance, one journey | | `--filter "Category=J4"` | journey size |

Acceptance tests drive the shipped bundle: run `pnpm build` first (and again after client changes). The fixture
fails with that instruction if the bundle is missing. Run one `dotnet test` or Playwright run at a time on a shared
host and wrap runs in `timeout 300`.

## One database per class

- A test class is one scenario on one database. The fixture clones a template database
  (`CREATE DATABASE ... TEMPLATE`), starts its own Kestrel instance of the real app against the clone, and drops the
  clone afterwards. Classes run in parallel; tests inside a class run in order.
- Independent scenarios are separate classes. Do not share state between classes.
- Tests inside a class are the steps of one story: mark each `[Test, Order(n)]`. A later step may rely on the
  earlier ones.
- **Seed vs schema clone.** `SeededAcceptanceTest` starts from `codex_seed_<hash>` (schema, migrations and `db/seed`).
  `EmptyAcceptanceTest` starts from `codex_schema_<hash>` (no rows): first-run states, or tests that build their own
  data. Both expose `DatabaseName`. In API tests the equivalents are `CodexDatabase.CloneSeedAsync()` and
  `CloneSchemaAsync()`.
- Templates are named by a hash of `db/` contents: editing any `db/` file builds a new template once.
- Doubles (fake clock source aside, see below) go in through `ConfigureTestServices` on the fixture. Production code
  has no test switches.

## Writing an acceptance test (copy this)

```csharp
namespace Codex.AcceptanceTests.Lanes.Plan;           // Lanes.<Lane>: the CI shard filter matches on this

[TestFixture]
public class PlanOutlineTests : SeededAcceptanceTest   // Codex.AcceptanceTests.Harness
{
    [Test, Order(1)]
    public async Task Plan_WhenOpened_ShouldShowTheOutline()
    {
        await Page.GotoAsync(Links.Plan(SeedIds.Books.PlayersGuide.ToString()));
        await Expect(new AppShell(Page).BookBar.Root).ToBeVisibleAsync();
    }
}
```

- Files go in `src/Codex.AcceptanceTests/Lanes/<Lane>/`. Namespace `Codex.AcceptanceTests.Lanes.<Lane>`.
  Lanes: Shell, Library, Plan, Write, Kb, References, Settings, MobileEditor (the mobile app's editor page, `/mobile-editor.html`; runs in the `Other` shard). Cross-lane journeys: `Journeys/`.
- Journeys carry a `[Category("J4")]`-style tag naming the journey map entry, one `[Order]` step per test.
- Selectors are `data-testid` only, through page objects. Shell page objects (`AppShell`, `LibraryShelf`, `BookBar`,
  `KbSidebarFrame`) are in `Pages/Shell/` and use only `TestIds.Shell`. A lane adds page objects under
  `Pages/<Lane>/` using its own ids.
- Navigate with `Links` (`Harness/Links.cs`): C# string builders for the routes and deep links in
  `docs/navigation.md` (`design/README.md`, "Moving between screens"). The TypeScript link builders cannot be
  called from C#, so keep `Links` in step with them.
- Wait for a locator (`Expect(...)`) or a response (`page.RunAndWaitForApiAsync(path, method, action)`).
- Expected relative labels: `ExpectedLabels.Relative(value)` / `ExpectedLabels.JustNow()`, which call
  `DisplayDate.Relative(value, TimeProvider.System)`. Time is never frozen: the database stamps comparison
  timestamps and the app uses `TimeProvider.System`.
- Naming: `Method_WhenCondition_ShouldOutcome`. Never weaken or delete an existing test.

## Writing an API test

Helpers in `src/Codex.WebApi.Tests/Support/` (namespace `Codex.WebApi.Tests`), one place each; extend them rather
than writing another:

- `ApiFixture` (seed clone) or `SchemaApiFixture` (bare schema plus JD) as the base class. Requests are signed in
  as JD by the test sign-in handler; override `SignedInAs` (`TestUser.Of(id)`, `TestUser.Anonymous`) or call
  `client.ActingAs(id)` to act as someone else, and `TestUsers.CreateAsync` adds a user (`docs/auth.md` → *Tests*).
- `TestRows`: every row insert, static over an open connection (`await using var db = await OpenDatabaseAsync();`
  then `TestRows.SingleSceneAsync(db)`, `ShelfBookAsync`, `ChapterAsync`, `SceneAsync`, `ProseAsync`, `LabelAsync`,
  `CategoryAsync`, `EntryAsync`, `BeatAsync`, `SourceAsync`, `UseAsync`, …). A new lane's rows go here.
- `response.ReadApiErrorAsync(HttpStatusCode.NotFound)`: asserts the status and that the body is exactly
  `{ code, error }`; returns the `ApiError`.
- `ShellApiFixture` (Library rows) and `BookContentFixture` (books with chapters, scenes, entries, sources) are thin
  `SchemaApiFixture` subclasses whose `Add…Async` methods call `TestRows`.

## Banned patterns

| Banned | Use instead |
|---|---|
| `WaitForTimeout`, sleeps | wait for a locator or a network response |
| `DateTime.Now` / `UtcNow` / `new Date()` for display logic | `TimeProvider` (C#), server-provided values (TS) |
| `[Retry]`, `[Ignore]`, `NoWarn`, `#pragma warning disable`, `eslint-disable`, `@ts-ignore` | fix the cause |
| test-only branches or props in production code | `ConfigureTestServices`, `page.route`, fixtures |
| non-`data-testid` selectors | the registry (`TestIds`, `testIds.ts`) |
| string-built SQL | Dapper parameters |
| `DELETE FROM` in `src/` (hard delete) | soft-delete with `archived_at` (link rows too; re-adding revives the row); exempt derived tables are listed in `NoHardDeletesTests` |
| EF Core | Dapper |
| compensating for a wrong contract (hardcoded `[]`, swallowed errors, fallbacks) | stop and report |
| editing a frozen file or another owner's file | stop and report |

## Traces

Each test records a Playwright trace; it is kept only when the test fails, at
`artifacts/playwright-traces/<full test name>.zip`. Open with
`cd src/Codex.Client && pnpm exec playwright show-trace <zip>`. CI uploads them per shard
(`playwright-traces-<shard>`).

## CI shards

The acceptance job runs one shard per namespace, filtered with a trailing dot so `Lanes.Plan` does not match
`Lanes.PlanX`: `Harness`, `Lanes.Shell`, `Lanes.Library`, `Lanes.Plan`, `Lanes.Write`, `Lanes.Kb`,
`Lanes.References`, `Lanes.Settings`, `Journeys`. Tests in any other namespace run in the `Other` shard (that is where
`Lanes.MobileEditor` runs), so nothing is skipped, but put tests in the listed namespaces. A shard with no tests passes.

`Journeys` splits by class-name prefix: `Journeys.J0`, `Journeys.J1`, and `Journeys.Rest` for every other journey.
The shards are grouped into four jobs balanced on test time. Each job uploads its timings as `acceptance-trx-<group>`,
so rebalance from those. `main`'s ruleset requires each job by name, so adding or removing a group means updating it.
