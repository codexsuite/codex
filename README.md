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

Version 1 is built and in use.

| Area | State |
|---|---|
| Library, Plan, Write, Knowledge base, References, Settings | Built. Each has API, component and acceptance tests. |
| Layout and print pipeline | Built. Quick preview runs in the web image. Proofs and Export for printer run in the worker image. All of them use versioned LaTeX template packages (`docs/print.md`). These features need Landlock ABI 4, see Configuration. |
| Manuscript import and export | Built. Import reads Markdown folders, Novelcrafter zips, `.docx`, pandoc folders and Codex backups (`docs/import.md`). Export covers the printer PDF, Word, EPUB, Markdown and a source archive. |
| AI drafts | Built. Each user connects their own provider in Settings, AI. There is no server-wide key (`docs/ai-integrations.md`). |
| Real-time updates | Built. A SignalR hub and a Postgres bridge show edits made by agents and other processes (`docs/events.md`). |
| Sharing | Built. An owner shares a book with another user, with a role (`docs/auth.md`). |
| MCP server | Built. `/mcp` over HTTP with personal access tokens, and a stdio server (`docs/mcp.md`). |
| Journeys J1 to J28 | Built as end-to-end tests. Keyboard-only variants exist for J1, J9 and J10 only. |
| Mobile app | The app is in `src/Codex.Mobile`. It has Plan, Write, Knowledge, Settings and Library screens. |
| Real-time co-editing, chat, review, plugins | Not built. A reference screen for collaboration is in `design/collab.html`. |

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

Configuration is by `CODEX_`-prefixed environment variables. See [Configuration](#configuration).

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

## Configuration

The `codex` (web) service takes the Database, First admin, Files and keys, Worker, AI, MCP and Logging groups. The `worker` service takes Database, Files and keys, Worker, Print and Logging. Set the Print group on the worker, because it runs PDF runs and has Tectonic. The web image only needs the Print variables for Quick preview and template thumbnails. `src/docker-compose.yml` shows a working set.

| Variable | Group | What it does | Default |
|---|---|---|---|
| `CODEX_DB_CONNECTION` | Database | Npgsql connection string to the PostgreSQL 18 database. Every session runs at UTC whatever the string says. | None. Required. |
| `CODEX_DB_MIGRATE` | Database | Applies the schema and migrations at start. The app never applies the seed. Set `false` on the worker, so only the web service migrates. | `true` |
| `CODEX_ADMIN_EMAIL` | First admin | With the password, creates the first admin on boot, or gives a password to an existing user who has none. Remove both after the first sign-in. | None |
| `CODEX_ADMIN_PASSWORD` | First admin | Password for that admin. Both variables must be set, or neither takes effect. | None |
| `CODEX_BLOB_ROOT` | Files and keys | Folder for uploads, renditions and exports. The web and worker containers share it. A relative path resolves against the app folder. | None. Required. |
| `CODEX_DATA_PROTECTION_DIR` | Files and keys | Folder for the key ring that encrypts users' AI keys. Store it outside the database, so a database dump alone cannot decrypt the keys. Share it with the worker. Without it, AI keys cannot be saved. | None |
| `CODEX_ROLE` | Worker | `web` serves the app and claims no print jobs. `worker` claims render and export jobs. `all` does both in one process. | `web` |
| `CODEX_EVENTS_LISTEN` | Worker | Whether this process listens for book events from other processes. Set `false` on the worker. The image does not set it. | `true` |
| `CODEX_AI_ALLOW_LOCAL_ENDPOINTS` | AI | `true` lets users point an AI integration at `http://` and private-network addresses, such as Ollama or LM Studio. Codex reads it on every call, so a change needs no restart. | Off |
| `CODEX_TEMPLATES_ROOT` | Print | Folder of the built-in layout template packages. A relative path resolves against the app folder. | `../../templates` (`/templates` in the images) |
| `CODEX_TEMPLATE_CACHE` | Print | Where template packages are unpacked from their stored zip. | `codex-template-cache` in the temp folder |
| `CODEX_RENDER_WORK` | Print | Parent folder of the per-job work folders. | `codex-render` in the temp folder |
| `CODEX_RENDER_TIMEOUT_SECONDS` | Print | Wall-clock limit for one Tectonic run. Pandoc gets a third of it. | `300` |
| `CODEX_RENDER_MAX_PDF_MB` | Print | Largest PDF a run may write, in megabytes. A bigger PDF fails the run. | `1024` |
| `CODEX_RENDER_MAX_PROCS` | Print | Process and thread limit for a run. The kernel counts every process of the user, so a shared host needs headroom. | `4096` |
| `CODEX_TECTONIC` | Print | Path to Tectonic, or a name looked up on `PATH`. | `tectonic` |
| `CODEX_TECTONIC_CACHE` | Print | Tectonic's bundle cache. Codex mounts it read-only during a run. | `~/.cache/Tectonic` (`/var/cache/tectonic` in the worker image) |
| `CODEX_PANDOC` | Print | Path to pandoc, or a name looked up on `PATH`. | `pandoc` |
| `CODEX_SANDBOX` | Print | Path to `codex-sandbox`, or a name looked up on `PATH`. There is no way to turn the sandbox off. | `codex-sandbox` |
| `CODEX_MCP_MAX_GENERATIONS` | MCP | How many Codex drafts one MCP client may start. Codex counts each token's drafts over a sliding hour and refuses the next with `rate_limited`. | `20` |
| `CODEX_SEQ_URL` | Logging | Seq server URL. Logs go to the console only until this is set. | None |
| `CODEX_SEQ_API_KEY` | Logging | API key for Seq. | None |

Print features need Landlock ABI 4, which arrived in Linux 6.7. `codex-sandbox` refuses to run without it. On an older kernel, including WSL2 on kernel 6.6, these fail. In the web process, Quick preview and template thumbnails. In the worker, proofs, Export for printer, and Word and EPUB export. Markdown export and the source archive do not use the sandbox, so they still work.

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
