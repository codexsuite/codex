<p align="center">
  <a href="https://codexsuite.app">
    <picture>
      <source media="(prefers-color-scheme: dark)" srcset="design/brand/codex-logo-on-dark.svg">
      <img alt="Codex" src="design/brand/codex-logo-on-light.svg" width="264" height="64">
    </picture>
  </a>
</p>

**Your whole book, in one place.**

Codex is an open-source writing studio for non-fiction books and series. Plan the structure, keep your research beside the draft, clear every quote for copyright and export a print-ready PDF, all without leaving the book. Novels are welcome too.

Website: [codexsuite.app](https://codexsuite.app)

<picture>
  <source media="(prefers-color-scheme: dark)" srcset="https://codexsuite.app/shots/write-dark.webp">
  <img alt="The Write screen: the manuscript editor with the scene summary beside the text and the knowledge base one click away" src="https://codexsuite.app/shots/write-light.webp">
</picture>

## Why Codex

A book is more than a document. Long non-fiction lives across outlines, notes, citations, permissions and print files. Codex keeps all of it attached to the manuscript, so nothing drifts out of date.

- **Structure you can see.** Acts, chapters and scenes on a board. Move a scene and the manuscript follows. Label what needs research and see at a glance what's left to write.
- **Research beside the draft.** People, laws, studies and stories live in a knowledge base that links itself into your text. Click a name to open everything you know about it.
- **From draft to printer.** Every quote checked against publisher rules, a bibliography built for you, and a print-ready PDF from real LaTeX templates when you're done.

## Six workspaces, one book

Every screen shares the same chapters, sources and entries, so you never copy anything between tools.

| Workspace | What it does |
|---|---|
| **Library** | Every book and series on one shelf. Series share a style guide and settings; progress shows against the word target. |
| **Plan** | A board of acts, chapters and scenes, or a plain outline. Scene summaries, word counts, labels like *Needs research*, and knowledge base entries pinned to scenes. |
| **Write** | A calm manuscript editor with the plan in the margin. Scene beats draft 200 to 600 words with AI; `@` mentions link to the knowledge base as you type; focus mode and a minimap. |
| **Knowledge base** | One entry per person, idea or source, with aliases so every mention links itself. See every scene that uses an entry, and a timeline for people and events. |
| **References** | Every source you quote, where you used it and whether you're cleared to print it. Quoted words are counted against publisher limits, permission letters are drafted for you, and the bibliography comes out in your citation style. |
| **Layout** | A live preview of the book in its print template, and a real proof when you need exact pages. Preflight flags low-resolution images; export with bleed and crop marks. |

Settings and style guides copy down to every book in a series. Changes save as you type, so there is no Save button to forget. Light and dark themes throughout.

## Status

The designs are done and version 1 is being built, in waves.

| Stage | State |
|---|---|
| Design | **Done.** Seven reference screens in light and dark, under `design/screens/`. |
| Skeleton, contracts, seed, test harness | **Done.** |
| Library, Plan, Write, Knowledge base, References, Settings | **Done.** Each lane has API, component and acceptance tests. |
| Layout and print pipeline, manuscript import, AI provider, realtime transport, MCP server | **Next.** The Layout tab is a placeholder for now. The Write screen's AI draft flow is built end to end, but production answers `not_configured` until the Anthropic provider lands. |
| Journeys J1 to J27 as end-to-end tests, keyboard pass, screenshot baselines | After that. |
| Real-time collaboration, chat, review, plugins, more exporters | On the roadmap. A reference screen for collaboration is in `design/collab.html`. |

Sign-in is by email and password, and there is no sign-up: an admin adds users from the user management page. See
[docs/auth.md](docs/auth.md).

## Stack

- **Server:** ASP.NET Core on .NET 10, thin MVC controllers (minimal APIs only for health, the SignalR hub and `/mcp`), Agoda.IoC for registration, Serilog (console, and Seq when configured). Warnings are errors; the clock is `TimeProvider` only.
- **Database:** PostgreSQL 18. The schema, additive migrations and seed under `db/` are applied by DbUp at startup. Npgsql + Dapper, no ORM.
- **Client:** React 19, Vite, Tailwind 4, TanStack Query, TipTap (ProseMirror) for the manuscript and summaries, dnd-kit, cmdk, citeproc. Inter for all text.
- **Prose:** stored as Markdown with a small set of directives (`::beat`, `::figure`, `kb:` mentions, `ref:` marks). A TypeScript parser and a C# helper agree on it; see `docs/prose-format.md`.
- **Tests:** NUnit for unit and API tests, Playwright for component and acceptance tests, all on a real Postgres and the real app. Selectors are `data-testid` only.
- **Ship:** two Docker images (the app, and a worker with pandoc and Tectonic for print) plus `postgres:18` in `src/docker-compose.yml`. Pushes to `main` publish the app image to GHCR.

## Run it

### With Docker

```sh
docker compose -f src/docker-compose.yml up --build
```

The app is on <http://localhost:8080> with an empty library. It applies the schema and migrations to the `codex` database on first start (DbUp keeps a journal, so restarts only apply what is new).

### For development

You need the .NET 10 SDK, Node 24 with pnpm, and a PostgreSQL 18 server (local, or the `postgres` service from the compose file).

```sh
dotnet run --project src/Codex.WebApi                  # API on :5688; creates the schema in the `codex` database named in appsettings.Development.json
cd src/Codex.Client && pnpm install && pnpm run dev    # SPA on :3000, proxies /api and /hubs to :5688
```

To work with the sample library (three series, fifteen books, a fully built *Product Engineering* manuscript with knowledge base and references), build the database from `db/` with the seed and tell the app not to migrate, because the script applies the scripts with `psql` and leaves no DbUp journal:

```sh
scripts/db-reset.sh                                    # drops and recreates `codex` (PGHOST/PGPORT/PGUSER/PGPASSWORD)
CODEX_DB_MIGRATE=false dotnet run --project src/Codex.WebApi
```

For a production-style run, `pnpm run build` emits the SPA into `src/Codex.WebApi/wwwroot` and the API serves it.

Configuration is by `CODEX_`-prefixed environment variables: `CODEX_DB_CONNECTION`, `CODEX_DB_MIGRATE` (default true), `CODEX_BLOB_ROOT` (uploads, renditions and exports), `CODEX_SEQ_URL`, `CODEX_SEQ_API_KEY`, `CODEX_ANTHROPIC_API_KEY` (AI drafts), `CODEX_ROLE` and `CODEX_EVENTS_LISTEN` (the worker container sets `worker` and `false`).

### Tests

```sh
dotnet build src/Codex.slnx                   # warnings are errors
dotnet test src/Codex.UnitTest                # pure helpers, contracts, architecture rules
dotnet test src/Codex.WebApi.Tests            # API tests: the real app on Kestrel over a cloned database
(cd src/Codex.Client && pnpm run build)       # once, and after client changes: acceptance tests serve this bundle
dotnet test src/Codex.AcceptanceTests         # Playwright against the real app; CI shards by namespace
cd src/Codex.Client && pnpm run lint && pnpm run typecheck && pnpm test && pnpm run test-ct
```

Database tests start a `postgres:18` Testcontainers container unless `CODEX_TEST_DB` points at a long-lived server. Each test class clones a template database and starts its own instance of the app, so classes run in parallel and nothing is mocked. The conventions are in `docs/testing.md`.

## Repository layout

| Path | What |
|---|---|
| `src/Codex.WebApi/` | Host, controllers, error mapping, health. Serves the built SPA. |
| `src/Codex.Application/` | Commands and queries per feature (`Features/<Feature>/`), database helpers, prose helpers. Embeds `db/` and `prompts/`. |
| `src/Codex.Contracts/` | Interfaces, DTOs and events shared across features. Features never reference each other except through here. |
| `src/Codex.Core/` | Pure helpers (display dates). |
| `src/Codex.Client/` | The React SPA. |
| `src/Codex.TestSupport/`, `src/Codex.UnitTest/`, `src/Codex.WebApi.Tests/`, `src/Codex.AcceptanceTests/` | Test infrastructure and the four test projects. |
| `db/` | `schema/`, `migrations/`, `seed/`, and the `prose-corpus/` the parsers are tested against. |
| `design/` | Design tokens (`tokens.css`, `tokens.json`) and the reference screens. Open any `screens/*.html` in a browser; there is no build step. |
| `docs/` | `navigation.md` (screens, routes, deep links), `testing.md`, `prose-format.md`, `ai-drafts.md`. |
| `prompts/` | Prompt templates and the model catalogue, served by `GET /api/prompts`. |
| `AGENTS.md` | The working rules for anyone, human or agent, changing the code: norms, where things are, banned patterns, frozen files. Read it before contributing. |

## Design

Palette **Nord Frost, deepened**; **Inter** for everything; **compact** density; light and dark. The app shows a lot of information at once, so it stays professional and quiet: small type, tight spacing, neutral surfaces, and colour only where it carries meaning. `design/README.md` has the tokens, the per-screen patterns, and how screens link together. The same tokens drive [codexsuite.app](https://codexsuite.app), whose [repository](https://github.com/codexsuite/codexsuite.app) also holds the logo, full-resolution screenshots and the social card.

## Not that Codex

Codex at codexsuite.app is an independent, open-source writing studio for authors. It is not affiliated with OpenAI or its Codex coding agent.
