# The Codex MCP server

Codex serves its tools to MCP clients (Claude Code, Claude Desktop) over two transports, both backed by the same
`Codex.Application/Mcp` code (#20, wave-3 decisions M1–M14 in its last comment):

- **stdio**: the `Codex.Mcp` process, for a client on the machine that can reach the database.
- **Streamable HTTP at `/mcp`** on the web host, for a client anywhere that sends a personal access token (#32, below).

## Running it over stdio

Build once, then point the client at the built dll. Don't use `dotnet run` as the client command: its build output
goes to stdout, which carries the protocol.

```sh
dotnet build src/Codex.Mcp -c Release
claude mcp add codex \
  --env CODEX_DB_CONNECTION="Host=127.0.0.1;Port=5432;Database=codex;Username=postgres;Password=postgres" \
  --env CODEX_DB_MIGRATE=false \
  --env TZ=UTC --env PGTZ=UTC \
  -- dotnet "$PWD/src/Codex.Mcp/bin/Release/net10.0/Codex.Mcp.dll"
```

Claude Desktop takes the same command, arguments and environment in its `mcpServers` config.

- `TZ=UTC` pins the process zone (the date labels it renders) and `PGTZ=UTC` the zone of any `psql` you run beside it.
  Postgres sessions the server opens are already pinned to UTC in code (`DatabaseServices.PinUtc`), so a missing
  `PGTZ` cannot move a day boundary; `TZ` is the one only the environment can set.
- `CODEX_DB_CONNECTION` is the only setting it needs. Set `CODEX_DB_MIGRATE=false` when the web host owns the
  database, which is the usual case. `manage_permission`, `draft_permission_letter` and the image tools also need `CODEX_BLOB_ROOT`, the web
  app's blob folder (see *Tools (phase 3: references)* and *Tools (phase 5: images, figures and openers)*), and the
  image tools' dpi needs `CODEX_TEMPLATES_ROOT`, the repo's `templates/` folder.
- It acts as one user, `CODEX_MCP_USER_EMAIL` (default `jd@codex.local`; it must be an active user), or as a personal
  access token's user with its scopes when `CODEX_MCP_TOKEN` is set (*stdio with a token*, below), through the same
  `ICurrentUser` as the API, so the access rule (`docs/auth.md`, #80) applies: it works in the user's own books and
  the books shared with them, at their role there (*Roles*, below); a book that is neither, and anything in it, is
  `not_found`, and its title never resolves. It runs no background work: migrations, renditions and the quote-flag
  scanner stay with the web host.
- Logs go to stderr. stdout is the protocol.
- One process is one session. A display number such as `"5.2"` that names a different scene than it did when this
  session last read the book fails with `stale_ref` instead of reading the other scene.

## `/mcp` over HTTP: personal access tokens (#32)

A client anywhere sends a personal access token: `Authorization: Bearer cdx_pat_…`. The token's user becomes
`CurrentUser` for the request, so the access rule applies, and its scopes are checked for each tool (below). Create
tokens on **Account → Agent access** (`/account`). For Claude Code:

```sh
claude mcp add --transport http codex https://codexsuite.app/mcp --header "Authorization: Bearer cdx_pat_…"
```

- A missing, unknown, revoked or expired token, or one whose user is disabled, gets **401** `unauthorized` with
  `WWW-Authenticate: Bearer realm="codex"`. So does any `Authorization` that isn't a bearer token.
- The `/api/*` routes never accept these tokens; they are for MCP only.
- `last_used_at` is written at most once a minute per token.

**Without a token, in Development only**, a request from this machine acts as the local MCP user (`CODEX_MCP_TOKEN`'s
user, else `CODEX_MCP_USER_EMAIL`), as before #32. In any other environment it gets 401. A request counts as local
when it:

- comes from a loopback address (`127.0.0.1`, `::1`, or `::ffff:127.0.0.1`);
- names a loopback `Host` (`localhost`, `127.0.0.1`, `[::1]`);
- carries no `Origin`, or the server's own origin: a loopback host on the port the request came in on. A page on
  another site, DNS rebinding and the opaque `null` origin are all refused;
- carries no proxy header (`Forwarded`, `X-Forwarded-For`, `X-Forwarded-Host`, `X-Real-IP`, `CF-Connecting-IP`,
  `True-Client-IP`, `Via`).

**Under docker compose, send a token** (requests to the published port come from the Docker bridge, not loopback), or
use stdio: run `Codex.Mcp` on the host against the compose `postgres` service, which publishes `5432` (the connection
string above). The app image doesn't ship `Codex.Mcp`.

### Scopes

Every token holds `codex:read`; the others are chosen when it is created (`McpScopes` maps each tool, and
`McpScopesTests` fails when a tool has no row).

| Scope | Tools |
|---|---|
| `codex:read` | `list_books`, `search`, `resume_work`, `get_outline`, `read_manuscript`, `get_draft`, `get_job`, `get_kb_entry`, `find_kb_usages`, `get_book_settings`, `get_clearance`, `export_references`, `list_images`, `get_image`, `preview_layout` (kind `proof` also needs `codex:write`), `export_for_printer` (mode `queue` also needs `codex:write`), `list_kb_entries`, `list_revisions`; every resource and every prompt |
| `codex:write` | `create_book`, `update_book`, `edit_outline`, `edit_prose`, `upsert_beat`, `upsert_kb_entry`, `link_kb_entry`, `submit_draft`, `resolve_draft`, `undo`, `upload_image`, `create_image_upload`, `place_figure`, `update_figure`, `set_opener`, `manage_kb_category`, `delete_kb_entry`, `merge_kb_entries`, `create_series`, `update_series` |
| `codex:references` | `cite_source`, `update_source`, `update_source_use`, `record_clearance_decision`, `manage_permission`, `draft_permission_letter`, `merge_sources`, `add_source`, `archive_source` |
| `codex:generate` | `draft_prose` (spends the author's model budget) |

A tool called without its scope fails `forbidden`, naming the scope in `message` and `field`. `undo` also needs the
scope of the tool whose write it reverses (undoing a `cite_source` needs `codex:references`). Holding
`codex:references` never skips the author's confirmation: `sign_off` and `accept_fair_use` stay UI-only (M6).

### Roles: books shared with the user (#80)

A book or series can be shared with other users to view, comment or write (Book settings → Sharing; there are no
sharing tools over MCP). A call needs **both** its scope and a high enough role on the book it touches: the scope says
what the token may do, the role what its user may do in that book. So a `codex:write` token on a book shared to view
fails `forbidden` (`message`: "This book is shared with you to view only."; no `field`, which names a missing scope),
and a book the user cannot open at all is `not_found`, exactly as an id that does not exist. Refs search the user's
own and shared books together: a title, `"<series>/<number>"` or an item inside a book resolves in either, a title both
share is `ambiguous` with candidates naming their owners, and a tool's default book (the most recently edited live book)
can be a shared one.

| Role needed | Tools |
|---|---|
| view (any role) | every `codex:read` tool, every resource and prompt; `preview_layout` kind `quick`; `export_for_printer` mode `preflight` |
| write | every `codex:write`, `codex:references` and `codex:generate` tool on the book it changes, `dry_run` included: `edit_outline`, `edit_prose`, `upsert_beat`, `upsert_kb_entry`, `link_kb_entry` (per link: the link's scene), `submit_draft`, `resolve_draft`, `draft_prose` (and `retry_of`), `undo`, `upload_image`, `create_image_upload`, `place_figure`, `update_figure`, `set_opener`, `update_book` (fields, publication, layout), the References tools; `preview_layout` kind `proof`; `export_for_printer` mode `queue` |
| owner | `update_book` with `series_number`, `series` or `make_standalone` (on the book and on the series it moves into); `create_book` into a series |

Comment gives nothing more than view over MCP (there are no comment tools yet). Series-scoped things follow the
series' role: a Knowledge base entry, category or image on a series needs write on the series, so a user with write on
one book of a series reads its shared entries but cannot change them. A draft is always written with the calling
user's own AI integration (`integration` names one of theirs), never the book owner's. `undo` reverses any write in a
book the user can still write in; once their role drops to view it is `forbidden`.

### stdio with a token

Set `CODEX_MCP_TOKEN` on the stdio process to act as that token's user with its scopes instead of
`CODEX_MCP_USER_EMAIL`'s with every scope. The process refuses to serve if the token doesn't work when first used, and
checks it again on every call: once it is revoked or expires, or its user is disabled, calls fail `unauthorized`.

The HTTP transport is stateless (SDK 2.2.0), so each request stands alone. `GET` and `DELETE /mcp` aren't served,
and there is no session to check `stale_ref` against. Over HTTP, display numbers always resolve to what they name now,
so use the ids that results return.

## Tools (phase 1: read only)

Every tool is annotated `readOnlyHint`, `idempotentHint`, and not destructive.

| Tool | What it does |
|---|---|
| `list_books` | The Library: series with their totals, then standalone books. Filter with `series` and `status`; sort with `sort` (`last_edited`, `title` or `series_number`). Lists the user's own books and the books shared with them, as the Library does; each book and series has `role` (`view`, `comment`, `write` or `owner`) and `owner` (`{ id, display_name }`). |
| `search` | Finds books, chapters, scenes, Knowledge base entries (by name or alias), sources and prose. Filter with `book` and `types`; `limit` is 1–100. |
| `resume_work` | A briefing: the last scene with its version and pending draft, every pending draft, the scenes waiting on the author, and References risks. |
| `get_outline` | Acts → chapters → scenes. `within` narrows it to one act or chapter. `include` takes `summaries`, `labels` and `word_counts` (the default) plus `kb_links`, `beats`, `plan_notes` (each chapter's and scene's planning notes; `summaries` covers chapters too), `archived` and `kb_index`. |
| `read_manuscript` | Prose in reading order, one block per scene with its `version`. Formats are `annotated` (the default) and `markdown`. Pages with `max_words` (default 4000, at most 20000) and `cursor`. A scene split across pages reads whole when its parts are joined: `markdown` parts concatenate as they are, and `annotated` parts join with a blank line. |
| `get_draft` | One AI draft: its text, model, attempt, template, tokens, and the scene's current version. |
| `get_kb_entry` | One Knowledge base entry: scope, aliases, body, fields, related entries, its source and its additions in book order. |
| `find_kb_usages` | Every scene that uses an entry, how it uses it, and counts by kind. |
| `list_kb_entries` | A book's Knowledge base entries (its own and its series', live only) in the screen's order, category then entry: `item`, `name`, `category`, `scope`, `aliases`, `tags`, `always_in_context` and `used_in_scenes` (distinct live scenes using it, over every book, as `get_kb_entry` counts). `category` (any case) narrows it; an unknown one is `not_found` and lists the book's categories. Pages of `limit` (default 100, at most 500) with an opaque `cursor`; `next_cursor` is absent (null) on the last page and `total` counts the entries in scope. A bad cursor is `invalid`. |
| `get_book_settings` | The merged style guide and the publication settings. |
| `list_revisions` | A scene's saved versions, newest first (the last 50, as History in Write shows them): each revision's `id`, `source` (`manual`, `autosave`, `ai_apply`, `restore`, `agent`, `import`, `replaced`), `label`, `detail` (an applied draft's model and attempt), `created_at`, `words` and `current` (the one whose text the scene holds now), plus the scene's `version` and `words`. `revision` (an id from the list) also returns that version's stored Markdown as `revision.markdown`; an id not in the list is `not_found`. Restoring is an ordinary edit: `edit_prose` with the old text, or Restore in Write. |

## Tools (phase 2: writes)

| Tool | What it does |
|---|---|
| `edit_outline` | A batch of ops, all or nothing: `move` (acts, chapters, scenes; `before`, `after` or `into`), `archive` and `restore` (acts, chapters, scenes; restore takes the archived item's id; an archived chapter or act takes its scenes with it, and restore brings back the ones that were live), `update` (a scene: `title`, `summary`, `plan_notes` (its planning notes, stored in `scene.plan_notes`; `@[...]` and `§[...]` resolve as in a summary), `status`, `add_labels`, `remove_labels`, `create_missing_labels`; a chapter: `title`, `summary`, `plan_notes`, `is_numbered`, where `is_numbered: false` takes it out of the numbering so later chapters renumber, and its `ch:<slug>` never changes; an act: `title`. A field that does not apply to the kind is `invalid`, naming it; undo puts back only the fields the op changed, `conflict` if they were edited since), `add` (`kind` act, chapter or scene; `title` and a place, plus `summary` and `labels` for a scene; a new act starts with one chapter holding one empty scene). Every ref is read against the outline as it was before the batch (M4), so an item the batch adds can't be named until the outline is read again. A scene or chapter `update` may also give `expected_summary` and `expected_plan_notes` (the text you read; null and empty match): if the stored value differs the batch fails `conflict` and nothing is written. Returns a result per op and `renumbered` chapters. `dry_run` reports the same without writing. |
| `edit_prose` | Exact-match replacements in one scene, all or nothing (M11): a missing `old`, or one that matches more than once without `occurrence`, fails the whole call. Needs `expected_version`, and a stale one fails with `conflict`. An edit may not add `::beat`, `::kb-addition` or `ref:` ids. `dry_run` returns the new body. |
| `upsert_beat` | Creates a beat at `at` (`"start"`, `"end"`, `{after: Anchor}`, `{before: Anchor}`; needs `expected_version`) or updates one: `instruction`, `target_word_count`, `is_hidden`, `context_add` and `context_remove`. |
| `draft_prose` | Codex writes a draft with the author's connected AI account (`beat`, or `scene` to continue at its end; `template`, `model` (default: the integration's default model), `integration` (an id or label among the author's own, default their default one; an unknown one is `invalid`), `target_word_count`, `retry_of`). With no integration connected it fails `not_configured`. Returns the generation id and a `job_id` (the same id) once the draft exists, with the scene `version` and `generations_left`; the text arrives in the background. Never applies (M12). Each token can start 20 per sliding hour by default (`CODEX_MCP_MAX_GENERATIONS` sets the limit, read when first used); past it the call fails `rate_limited` (M9). Recorded `created_via = 'mcp'` with the client's name. |
| `submit_draft` | Stores prose the agent wrote as a pending draft (`beat` or `scene`, `text`). No model label (`model = 'client'`), `client_name` kept, no generation budget used. Text containing `::beat`, `::kb-addition`, `::figure` or a `ref:` link fails `invalid`. |
| `resolve_draft` | `apply` (needs `expected_version`, stale fails `conflict`) or `discard` a pending draft; both undoable. A draft still being written, or already resolved, fails `conflict`. |
| `get_job` | A draft job's state (`running`, `done`, `failed`, plus the draft's status and words; read the text with `get_draft`), else an export job as the HTTP API reports it. |
| `upsert_kb_entry` | Creates (`book`, `name`, `category`; `scope` book or series; `create_category`) or updates (`entry`) a Knowledge base entry: `aliases`, `summary`, `body`, `fields`, `tags` (replaces the set), `always_in_context` and `exclude_from_auto_context`. A name or alias another entry in scope has fails `ambiguous` unless `allow_duplicate`. `get_kb_entry` returns `tags` and `exclude_from_auto_context`. `related_add` / `related_remove` (entries by id, name or alias, resolved in the entry's scope before anything is written: unknown is `not_found`, outside the scope `out_of_scope`, the entry itself or one in both lists `invalid`) relate and unrelate pairs on create or update; a pair already in the wanted state changes nothing and records no step. Undo archives a created entry (its pairs go with it), or puts an updated one's name, summary, body, aliases, fields, always-in-context, and the tags and exclusion when the call changed them, back (not its category, nor a category created for it) and unrelates or relates back exactly the pairs the call changed. Nothing reads `exclude_from_auto_context` yet: a draft's context is the beat's own entries plus the always-in-context ones. |
| `manage_kb_category` | One Knowledge base category of `book`, by name (`category`), one `op` per call and one change set. `create` (`category` is the new name; `colour` optional, default `category-research`; last in order); `rename` (`name` and/or `colour`; a name the book or series has is `conflict`); `reorder` (exactly one of `before` or `after` another category's name, or `position` `"first"`/`"last"`; the book's own categories and its series' are ordered separately, so an anchor from the other side is `invalid`); `delete` (a category with live entries is `conflict` naming the count, unless `move_to` names a category of the same side: the entries move there, in order, to the end, and the category is deleted in one transaction; a delete is soft: the category keeps its row with `archived_at` set, its archived entries stay in it, the name can be used again, and nothing is hard-deleted). `dry_run` reports without writing. A call that changes nothing records no undo. Undo puts the name and colour back (while they still read what the call set), puts a category back after its old predecessor (while its neighbour is unchanged), archives a created category (while it is empty), or brings a deleted one back (clears `archived_at`: same id, name, place and Details fields) and moves its entries back (`conflict` if a live category has its name again or an entry moved since). A deleted category reads as unknown (`not_found`) to every other tool. Annotated destructive. |
| `link_kb_entry` | One result per link: `scene` (chip on the Plan card), `mention` (the first plain use of `anchor.text` becomes a mention chip), `addition` (`scene`, `at`, `text`, `expected_version`), `context` (`beat`); `unlink` removes scene, mention and context links. A link whose scene is outside the entry's scope fails that link with `out_of_scope` and the others still run (`failed` counts them). `source` links fail `invalid`: link an entry to a source with `update_source`'s `kb_entry` field. |
| `undo` | Reverses one write by its `undo_id` within 24 hours (M14). Prose edited again since fails with `conflict`, and nothing is undone. |
| `delete_kb_entry` | Deletes a Knowledge base entry (`entry`: id, name or alias; `book` scopes a name; another user's entry is `not_found`). A soft delete in one transaction, the Plan screen's Delete: every mention in scene prose, summaries, beats and other entries becomes plain text, its timeline notes are removed and its scene, beat-context and related links are dropped. The scene bodies get the agent's revision (history shows what they read before). `dry_run` returns `mentions`, `scenes`, `notes` and `links` without writing or an `undo_id`. Annotated destructive. Undo brings the entry, the mentions, notes and links back, but only while nothing the delete touched has changed since (a scene edited, a summary, beat or entry text rewritten): otherwise `conflict`, nothing is undone. |
| `merge_kb_entries` | Folds the duplicate `entry` into the entry to keep, `into` (a name is looked up in the duplicate's scope; the target must be in the same book or the duplicate's series, else `invalid`; the same entry is `invalid`; another user's entry is `not_found`). One transaction, the Plan screen's Merge: every use of the duplicate (mentions, printed spells and stat blocks, summaries, beats, other entries' text) points at the kept entry, scene links, beat context, timeline notes, related links and sources that named it move, the kept entry gains the duplicate's name and aliases as aliases and its body under "Merged from <name>" (its summary and body stay), and the duplicate is archived. The scene bodies get the agent's revision. `dry_run` returns `scene_links`, `additions`, `related` and `new_aliases` without writing or an `undo_id`. Annotated destructive. Undo un-archives the duplicate and puts every text, link, note, reference, alias and the body back, but only while nothing the merge touched has changed since (a scene edited, the kept entry's body or aliases changed, a moved link or note removed): otherwise `conflict`, nothing is undone. |

Text an agent writes in a scene summary (`edit_outline` update) or a beat `instruction` may name entries as `@[Name]`; the server stores them as `[Name](kb:<id>)` and fails the call (`not_found`, `ambiguous`, `out_of_scope`) for a name it cannot resolve before anything is written. `§[Chapter]` names a live chapter by title and is stored as `[Title](chapter:<id>)`; an unknown title is `not_found` and one two chapters share is `ambiguous`, also before anything is written.

**Undo is checked first.** `undo` claims the change set (so two undos can't both run), then reads every step's precondition
before running any: the scene is still archived, the summary still reads what the agent wrote, the chapter is still where it
was put, the draft is still pending. If one fails the call is a `conflict`, nothing is written and the id can be used again.

Write tools set `destructiveHint` where they archive or replace text, and none is idempotent. Every write result
carries `version` (the scene's, when prose changed), `side_effects`, `affected` and `undo_id`.

**Provenance** (migration `202610211000_L9_mcp_provenance.sql`): each write records a `change_set` row (book, tool,
summary, `client_name` from the client's `initialize`, the session, and the undo steps), for structure and Knowledge
base writes as well as prose. Before an agent's first prose edit to a scene in a session, a `prose_revision` with
`source = 'agent'` keeps the body as it was (M12: one per scene per session), so Restore in Write is one click away.
Prose writes use the `IProseStore` origin `agent`. `reference_event.source` gains `agent` (and `client_name`) and `ai_generation` gains
`created_via` and `client_name` for the tools still to come. A write made with a personal access token also records the
token's id and name (`api_token_id`, `api_token_name`) on `change_set`, `prose_revision`, `reference_event` and
`ai_generation` (#32), so history shows which agent did what.

**Draft jobs.** `draft_prose` runs the generation on a plain background task owned by a singleton (`McpDraftRunner`), not a
hosted service: the stdio host drops every `IHostedService`, and the web host's `/mcp` serves the same tools. It returns once
the draft row exists and the model has begun to answer (the first token's latency, since `DraftGenerator` creates the row and
reads the first chunk together); a refusal before that (unknown template, an invalid model, `not_configured` when no AI integration is connected) is an ordinary tool error.
The task finishes the stream through the same `DraftGenerator` the Write screen uses, so the row ends `pending` (or `failed`),
and stops, keeping the text so far, when the host shuts down. The budget is in memory. A call with a personal access token
counts against that token over a sliding hour (#32: each `codex:generate` token gets `CODEX_MCP_MAX_GENERATIONS` an
hour). Without a token, a stdio process is one session; tokenless HTTP from this machine has no session, so all such
clients share one allowance of the same size over a sliding hour.

A batch whose ops are all valid but whose commands fail part way is rolled back
with the same steps undo uses: an added act or chapter is archived again, an archived one restored and put back, a renamed or edited one set back.

Refs follow #20 2.1: a book by id, title or `"<series>/<number>"`; an item by id, `"5"`, `"5.2"`, `"act:2"` or
`"ch:<slug>"`; an entry by id, name, alias or `"e:<slug>"`. Every result has `ok` and an `item` with `id`, `kind`,
`label` and `open_url`. The `open_url` is the same URL the UI's link builders make
(`src/Codex.TestSupport/Fixtures/links/links.json` pins both sides). A series entry opens in a live book of its
series.

**Subscriptions.** A client on protocol 2026-07-28 or later opens `subscriptions/listen` with
`notifications.resourceSubscriptions` (`McpSubscriptions`). The server acknowledges the URIs it honours, holds the request
open and sends `notifications/resources/updated` (tagged with the request id in `_meta`) when an `IBookEvents` event changes
one; cancelling the request ends the subscription. Only the six resources can be subscribed to, and only in books the
caller can open (their own and those shared with them): any other book or scene, an unknown id or any other URI fails
`not_found` before the acknowledgement. A change
announces the resource and the client re-reads it: `ProseSaved` announces `codex://scene/{id}` (that scene), the book's
`outline` and `references`; `StructureChanged` the `outline` and `references`, and for `kb` every subscribed
`codex://kb/{id}`; `BookMetaChanged` the `outline`, `style-guide` and `codex://books`. Resources are matched against the
books the caller owned when the request began, so a book created mid-subscription is not announced on `codex://books`.
The notifications ride the listen request's own response stream, so they work on stateless HTTP as well as stdio (`McpSubscriptionHttpTests`
subscribes with one token and hears a write made on another HTTP request). The stdio process drops the Postgres event listener, so it announces only its own session's
writes; a write by the web app or another agent is heard by the web host's `/mcp`, which listens (`docs/events.md`). The
retired `resources/subscribe` is not handled and `resources.subscribe` is not advertised, so a client on an older
revision re-reads. The SDK 2.2.0 client has no `subscriptions/listen` helper (`SubscribeToResourceAsync` sends the retired
call), so the tests send the request themselves (`McpListen`). Not announced: a References change that raises no book
event (a source added or rechecked).

**Resources:** `codex://books`, `codex://book/{id}/outline`, `codex://book/{id}/style-guide`, `codex://scene/{id}`,
`codex://kb/{id}` and `codex://book/{id}/references`. Each one returns the JSON of the tool that reads the same thing.
**Prompts** (slash commands; each reads its data with the tools in-process and returns instructions, so they need only `codex:read`):

| Prompt | Arguments | Does |
|---|---|---|
| `/resume` | `book?` | J1: the `resume_work` briefing, then offers the first pending draft. |
| `/draft-scene` | `scene`, `book?` | J5 and J6: reads the scene (`read_manuscript`), proposes beats, and on agreement adds them with `upsert_beat` and starts a `draft_prose` per beat. Drafts stay pending; nothing is applied. |
| `/print-readiness` | `book?` | J12: hands over `get_clearance` and works through the sources one at a time (risks, needs check, flags, uncited), then `preview_layout` and `export_for_printer` with `preflight`. |
| `/triage-references` | `book?` | J11: walks the unflagged-quote inbox (`not_a_quote` or `cite_source` `from_flag`) and the uses added since sign-off. |

`/print-readiness` and `/triage-references` never sign off or accept fair use: those stay the author's call in References (M6).

## Tools (phase 4: books)

| Tool | What it does |
|---|---|
| `create_book` | Creates a book over the Library's create-book command: standalone, or in a series (`series`, the next number unless `series_number` says otherwise, with the series' Knowledge base shared). It starts with one empty act. Returns the book with its Plan URL, `affected` and `undo_id` (undo archives the book). An empty title is `invalid`, an unknown series `not_found`, a taken number `conflict`. |
| `update_book` | Changes a book over Settings' commands: `fields` (title, subtitle, author_name, description, status, target_word_count, style_guide), `series_number` (the rest renumber), a move (`series` or `make_standalone`: the series' Knowledge base is copied onto the book, so it fails `needs_confirmation`, writing nothing, until the call is repeated with `confirm` after a `dry_run`) and `publication` (citation_style, quote_word_limit, publisher_name, planned_year, print_run, formats, territory) and `layout` (`template` slug, `trim`, `options` as `{ key: value }`: the Layout screen's choice, over `ILayoutService`; a member left out keeps what the book has, and a bad template, trim or option fails `invalid`). `dry_run` reports without writing. Undo restores fields, number, publication settings and the previous layout selection; a call that moves a book between series cannot be undone. |
| `create_series` | Creates a series you own (`POST /api/series` underneath): `name` (blank or over 200 characters is `invalid`), optional `description` and `author_name`, and optional `books` (refs, in order: they become books 1, 2, ... of it, through the same move as `update_book`, so a book leaving another series gets that series' Knowledge base copied onto it). With `books` the call fails `needs_confirmation`, writing nothing, until repeated with `confirm` after a `dry_run`; a series ref in `books` or a repeat is `invalid`. Returns the series with its Library link, `books`, `affected` and `undo_id`. Undo archives the series while it is still empty; a call that also moved books cannot be undone (`blocked`, `undo_unsupported`). |
| `update_series` | Tidies a series (`series_ref` is its id or title): `name`, `description` (empty clears it), `order` (every live book of the series as refs, in the new order; they renumber 1, 2, ...; a missing, repeated or foreign book is `invalid`) and `archive` (a soft delete: the series sets `archived_at` and its books' rows are left alone, keeping `series_id` and number; while it is archived they read as archived in the Library, `list_books`, `find` and refs, and writes into one are not blocked). `dry_run` reports without writing. Undo puts the name, description, order and archive back (a restored series brings its books back as they were; a book archived on its own stays archived) while they still read what the call left, else `conflict`. |

`update_book` has no `expected_version`: Settings' commands take none. A layout's template version is not chosen: the book keeps the version it pinned.

`get_job` answers a `draft_prose` job and, for any other id, an export job as `GET /api/jobs/{id}` reports it (`queued`,
`running`, `done` or `failed`, with progress and step). `McpExportJobTests` pins that with rows inserted in the test.

| Tool | What it does |
|---|---|
| `preview_layout` | "How will this look in print?" Over the Layout view and proof commands. `scope` is a scene, chapter or act ref (omit for the whole book); `template` tries another layout without changing the book. `kind` `quick` (default) renders the Layout preview and returns the template, trim, an estimated page count (the Layout bar's formula) and `warnings` (template notices, unknown directives, raw LaTeX, missing assets); it needs pandoc and fails `not_configured` without it. `kind` `proof` queues a one-chapter PDF (`scope` is a chapter, or a scene's chapter; no scope or an act is `invalid`) and returns `job_id` (read it with `get_job`), or the earlier proof of unchanged content (`cached`). A proof writes a job row, so it also needs `codex:write`; a quick preview writes nothing. `open_url` is the Layout screen at the scene or chapter. |
| `export_for_printer` | "Build the print PDF", over the Export for printer commands. `mode` `preflight` returns `verdict`, `can_queue` and the `blocks` (copyright risks from the print gate, missing images), `warnings` (images under the target dpi, unknown directives, raw LaTeX the template skips) and `notes`, each with the scene or source it is about; it queues nothing. `mode` `queue` preflights again and queues the export, returning `job_id`, `state`, `affected` and a side effect naming `get_job`. Any block, a copyright risk included, fails `blocked` and queues nothing (no draft export over MCP); an export already queued or running fails `conflict`. Settings: `template`, `trim`, `bleed` (`none`, `3mm`, `0.125in`), `crop_marks`, `image_quality` (`print` 300 dpi, `screen` 150 dpi); a bad one is `invalid`. A queued export is built by the worker and has no `undo_id` (cancel it in Layout). Preflight needs only `codex:read`; `queue` also needs `codex:write` (forbidden otherwise, naming it in `field`). |

The print toolchain (pandoc, Tectonic) lives in the web and worker processes. Neither tool waits for a compile: both stop at the queued job, and `get_job` reports it (`kind` `print` or `proof`). A stdio process needs `CODEX_TEMPLATES_ROOT` (the repo's `templates/`) for preflight and proofs, like the image tools' dpi.

## Tools (phase 3: references)

Adapters over the References lane's commands and the Cite lane's `ICitationCommands`; nothing here writes SQL of its own
except choosing who a History line is written as. A source is named by a `SourceRef`: an id, a marker (`"#4"`), a cite key or a title, in `book`
(the most recently edited live book when omitted; an id names its own book). No match is `not_found` with the close ones,
two matches `ambiguous`. Every History line a call writes is recorded as `reference_event.source = 'agent'` with the
client's name: the References commands take an `EventProvenance` argument (default: the author, `'user'`, which is what the web
endpoints pass) and write it when they insert the line; nothing relabels a line afterwards. Use ids and flag ids come from `get_clearance`. A `next_action` is one of `reduce_quoted_words`,
`request_permission`, `chase_permission`, `cut_or_request`, `sign_off`, `decide_fair_use`, `set_licence`, `fix_identifier`,
`review_new_use`, or `fill_details` (the citation is incomplete); it is absent when nothing is needed.

| Tool | What it does |
|---|---|
| `get_clearance` | Read-only. Without `source`: the `summary` strip (`cited`, `cleared`, `needs_check`, `risks`, `not_cited`, `total`, `print_blocked`, `unflagged_quotes`), every source with marker, `status`, `reason` (in the References panel's words, "342 of 300 words, no request"), `reason_code` and `next_action`, and the unflagged-quote inbox as `flags` (scene, excerpt, best-guess source, match percent, locator). `status` filters the sources; `include` takes `uses`, `checks`, `permission`, `history` and `flags`. With `source`: that source as `focus` plus a `detail` of its uses (marking those `new_since_sign_off`), identifier checks, permission request and history. |
| `cite_source` | Wraps words already in a scene as a source marker: `scene` + `passage` (`{ text, occurrence? }`) + `expected_version`, or `from_flag` (an inbox flag: its own words and locator; a different `locator` is `invalid`). `source` is a ref, `{ isbn \| doi \| url }` (looked up through `ISourceLookup`, which refuses private addresses; matched against the book's sources, Not cited ones included, else added) or `{ new: { kind, title, authors, year, publisher, … } }`; `force_new` skips only the lookup's match, since the Add-source command still refuses an identical identifier. Returns the `use`, `marker`, `markers_shifted`, `status_before` and `status` with the `reason`, and `source_how` (`ref`, `matched` or `added`). A stale `expected_version` is `conflict`; a passage that is not in the scene is `invalid`, and a source the call added is archived again. `dry_run` resolves and reports. Undo restores the scene's prose and archives a source the call added; a citation from a flag cannot be undone (the flag stays resolved). |
| `update_source` | Changes a source: `fields` (kind, title, authors, year, publisher, edition, isbn, doi, url, accessed_on, licence, rights_holder, kb_entry, cite_key, permissions_contact; an empty string clears; a cite key is one word and unique in the book, else `invalid`), `recheck` (isbn, doi, url) and `accept_redirect` (after a re-check reports a moved address). Returns `status_before`, `status` and the `reason` before and after, and each re-check's outcome. Undo puts the fields, Codex link, cite key and permissions contact back while they still read what the call left (an edit by the author since is a `conflict`). `accept_redirect` looks for a moved address first (a read-only lookup), so a call with nothing to accept is `invalid` and writes nothing. `merge_into` is `invalid`: merge duplicates with `merge_sources`. Use-level changes are `update_source_use`. Annotated destructive. |
| `update_source_use` | Changes one use of a source (`use` is its id from `get_clearance`): `kind` (quote, paraphrase, data, figure, lyric), `locator` (empty clears), `rewrap` (`{ text, occurrence? }` + `expected_version`: moves the use's marker onto the words you wrote in its scene, keeping the same use, so its kind, locator and history stay; the old words become plain text, and an archived use whose marker is gone comes back) or `remove` (unwraps the marker, which archives the use; `expected_version` optional, a stale one is `conflict`). `remove` can't be combined with the others; `rewrap` without `expected_version` is `invalid`. A stale `expected_version` is `conflict`, a `rewrap` text that is not in the scene is `invalid`, a use in a book the user cannot open is `not_found` (one they can only view is `forbidden`). Returns the source's clearance before and after (`status_before`, `status`, `reason`, `quoted_words`) and the scene's new `version` when prose changed. `dry_run` checks and reports without writing. Undo puts kind, locator and a rewrap's prose back while they still read what the call left; removing a use cannot be undone. History lines are the agent's. Annotated destructive. |
| `record_clearance_decision` | `reopen` takes back a sign-off and `not_a_quote` (with `flag`) dismisses a flag for good (its note is not stored). **Copyright decisions are UI-only over MCP (M6, confirmed by Joel 2026-10-22):** `sign_off` and `accept_fair_use` (which needs `note`) never write. The agent prepares them: the call comes back `blocked` with `rule: author_decision`, the summary of uses and quoted words in the `message`, and the `open_url` of the source in References, where the author signs off. The other rule checks still come first: signing off a source that is still a copyright risk is `blocked` (`rule: copyright_risk`, also with the `open_url`) in the panel's words, "Quoted prose is over the limit: 342 of 300 words." Undo of `reopen` is `blocked` (`undo_unsupported`): re-signing a source is the author's call, so the agent cannot put a sign-off back; `not_a_quote` cannot be undone either. |
| `draft_permission_letter` | Drafts the permission request for a source: the letter comes from the uses and the book's print run (`letter` and `to_address` override). Codex doesn't send mail; the author sends it and `manage_permission` `mark_sent` records that. A source with a live request (draft, sent or granted) is `conflict`; a blank `letter` is `invalid` before anything is written. Returns the request's status, letter and reminder and the source's clearance. Undo withdraws the request, and only while it still reads what the agent drafted (the author editing the letter or marking it sent makes undo a `conflict`). Needs `CODEX_BLOB_ROOT` (see below). |
| `manage_permission` | `update` (a draft's `letter`, `to_address`), `mark_sent` (Codex doesn't send mail; this records that the author did, and sets a reminder two weeks out), `set_reminder` (`remind_at`, `yyyy-MM-dd`), `record_refusal` (`note`) and `withdraw`; draft the request first with `draft_permission_letter` (`action: "draft"` is `invalid`). Returns the request's status, letter and reminder and the source's clearance. A transition the request's status doesn't allow is `conflict`, and so is any action on a source with no live request. `record_grant` is always `blocked` (`rule: signed_letter_is_a_file`): a grant needs the signed letter as a file, which can't be attached over MCP, so it carries the References link. None of its actions can be undone (`blocked`, `undo_unsupported`). A blank `letter` or a bad `remind_at` is `invalid` before anything is written. The tool needs `CODEX_BLOB_ROOT` (see below) and is `not_configured` without it. |
| `add_source` | Adds a source to References without citing it (J9B's "add it for later"). `source` takes `cite_source`'s two non-ref forms: `{ isbn \| doi \| url }` (looked up through `ISourceLookup`, M8, and matched against the book's sources, Not cited ones included) or `{ new: { … } }`. A match answers that source (`how: "matched"`) and writes nothing; `force_new` adds a lookup result anyway, though never a second source with the same identifier. A new source is `not_cited` until a use names it. A ref string is `invalid` (cite it with `cite_source`). `dry_run` looks the identifier up and writes nothing. Undo archives a source the call added. |
| `archive_source` | Removes a source, as Remove in the References panel (`ISourceCommands.ArchiveAsync`): every use's marker is unwrapped into plain text (one agent revision per changed scene; later markers renumber), the uses and the source are archived, and open unflagged quotes that suggested it lose that guess. Never a hard delete (Part 6). Returns `uses` and the `scenes` whose prose changed. `dry_run` reports them without writing. Undo brings the source back, restores each scene's prose (which brings the uses back with their links) and points those flags at it again, while the scenes are unedited since; otherwise `conflict`, and nothing is undone. |
| `merge_sources` | Folds the duplicate `source` into the source to keep, `into` (a ref in the same book; a source in another book, or the same source, is `invalid` on `into`; a source in a book the user cannot open is `not_found`). One transaction: the duplicate's uses move to the kept source (archived uses too, so the ids the prose names stay valid), and the kept source's empty fields (authors, year, publisher, container title, volume, issue, pages, edition, ISBN, DOI, URL, accessed date, rights holder, permissions contact, Codex entry, licence while `unknown`) are filled from the duplicate; fields that have a value stay. The duplicate's live permission request moves too when the kept source has none, with its letters; otherwise it stays on the archived duplicate. Open quote flags that guessed the duplicate now guess the kept source. The duplicate is archived (`merged_into_id` set) and both sources get a History line as the agent. **The prose is not edited:** a `ref:` link holds the use's id, not the source's, so every link keeps working; markers renumber because they are counted over the sources. A moved live use counts as a new use, so a signed-off kept source reopens, and sign-off and fair-use decisions stay each source's own (neither is copied). `dry_run` does the same work and rolls it back: it returns `uses_moved`, `filled_fields`, `permission_moved` and the kept source's `status_before` / `status` / `reason`, and no `undo_id`. Annotated destructive. Undo un-archives the duplicate, moves its uses back (with their original `changed_at`), puts the filled fields back and moves the permission request back, but only while nothing the merge touched has changed since (a moved use retyped, lengthened or removed, a filled field edited, the request sent, answered or withdrawn, a source archived again): otherwise `conflict`, nothing is undone. Attachments other than a moved request's letters (web archive, source text) stay on the archived duplicate. |
| `export_references` | Read-only. `format` is `bibtex`, `ris`, `csl_json`, `permissions_log` (CSV) or `bibliography`; the content comes inline with `entries` and `file_name`. Cited sources only, unless `include_uncited`. `bibliography` is a plain author-year list with `style_applied: false`: no server-side citation formatter exists (citeproc runs in the browser and Print), so for the book's style take `csl_json`. |

**`CODEX_BLOB_ROOT`.** A permission request keeps its signed letter in the blob store, so `manage_permission` and `draft_permission_letter` (and undoing a
draft) need the same folder the web app uses. The other tools don't, and a stdio server started without it still serves them.

## Tools (phase 5: images, figures and openers)

Moving a book's art into Codex (#65). The image bytes never go through a tool call: base64 in an argument would make the
model emit every byte as output tokens. They reach Codex out of band, and the tool call carries only a path or a size.
Both upload routes end in `IAssetService.UploadAsync`, so the limits (PNG, JPEG or WebP, at most 25 MB and 64
megapixels), probing and renditions are the Assets panel's. An upload is `processing` until the web host's worker has
made the web, proxy and print renditions.

| Tool | What it does |
|---|---|
| `upload_image` | **stdio only.** `Codex.Mcp` reads the file at `path` (absolute, on the machine it runs on) and stores it for `book`, or with `scope: "series"` for its series. Takes `source`, `prompt` and `licence` notes (kept in `asset.meta`). Returns the `image` (id, filename, scope, status, bytes, pixel size, `open_url`). |
| `create_image_upload` | **HTTP only.** Takes `filename`, `content_type`, the exact size in `bytes`, `book`, `scope` and the same notes, and returns a single-use `upload_url` valid for 10 minutes, with a `curl` line: `curl --fail -T <file> <upload_url>`. The `PUT` answers 201 with the image. |
| `list_images` | Read-only. The book's and its series' images, newest first, with status, bytes, pixel size and the effective dpi at `placement` (default `column`) in the book's layout. `filename` narrows them; match on filename and bytes to skip files already uploaded. |
| `get_image` | Read-only. One image's status, size and dpi. With `view: true` the result also carries the web rendition (about 1600 px) as MCP image content, so the agent can look at it to write alt text or a caption; until the rendition exists it carries a `note` instead. |
| `place_figure` | Places an image in a scene: `scene`, `at` (`"start"`, `"end"`, `{after: Anchor}`, `{before: Anchor}`, as `upsert_beat`), `asset`, `placement` (`column`, `page`, `spread` or `float-top`), `caption`, `alt_text` and `expected_version`. Writes the `figure` row and its `::figure` line in one transaction (`FigureCommands`); a stale version fails `conflict` and writes nothing. Undo takes the line out, which archives the figure. |
| `update_figure` | Changes `caption`, `alt_text` (`""` clears either), `placement` or `asset`; undo puts the old values back while the figure still reads what the call left. Or `remove` (with `expected_version`, and no other change): the line is taken out and the figure archived, as deleting the card in Write does; undo puts the line back, which revives it. |
| `set_opener` | Sets an act's (a part opener, `\part` with its art in the #13 import mapping) or a chapter's opener art: `target` (`"act:2"`, `"5"`, `"ch:<slug>"` or an id), `asset`, `caption`; `clear` removes it. Undo puts the previous opener back. |

An image must belong to the book or its series (else `invalid`), and figures and openers are the commands the HTTP API
serves too: `POST /api/scenes/{id}/figures`, `PATCH /api/figures/{id}`, `POST /api/figures/{id}/remove`,
`PUT /api/acts/{id}/opener` and `PUT /api/chapters/{id}/opener`, which Write's `/` → Figure… and figure card and Plan's
Opener art… use. Layout's
preview, preflight and export read figures and openers from their rows, so a placed figure shows there with its
renditions and its dpi status. `submit_draft` and `edit_prose` still refuse a `::figure` line: a figure is created by
`place_figure`.

**Upload links.** `create_image_upload` stores only the SHA-256 of the link's secret (`asset_upload`), with the book (or
series), the user and the personal access token the call ran under. `PUT /api/asset-uploads/{token}` takes no cookie: the
link is the credential. It refuses a declared `Content-Length` over 25 MB, or other than the size the link was made for,
before reading the body (400), and a link that is unknown, already used, expired, or whose token was revoked or expired
or whose user was disabled (404). A failed upload gives the link back for another try; a successful one spends it.

The two upload tools are transport-specific: `upload_image` reads the server machine's disk, so it is offered over stdio
only, and `create_image_upload` hands out a URL on the web host, so it is offered over HTTP only (`McpTransportTools`
leaves each out of the other's `tools/list` and refuses a call to it as `invalid`, `field: "name"`). The image tools
need `CODEX_BLOB_ROOT` (`not_configured` without it). Their dpi needs the book's layout template: a stdio process
without `CODEX_TEMPLATES_ROOT` lists images without `dpi` and says why in `dpi_note`. Uploads are not undoable: an
unplaced image changes nothing in the book. Fetching an image from an `https://` URL is not offered (it would need SSRF
guards).

## Journey tests

`src/Codex.Mcp.Tests/Journeys/` holds the agent's version of each journey from #16, one class per journey, tagged
`[Category("J<n>")]`: J1 (pick up), J2 (series book), J3 (new book), J4 (restructure), J5 (draft from a beat), J6 (fill an
empty scene), J7 (Knowledge base entry), J8 (where used), J9 (cite a source), J10 (clear a copyright risk: paraphrase, and ask permission), J11 (triage unflagged quotes), J12 (get ready to print), J13 (focus, read side) and J14 (find a book). Each runs the real
`Codex.Mcp` process over stdio and, on the same clone of the seed, the real web host (`JourneyServer`), so a test checks the
result envelope (`item`, `version`, `affected`, `side_effects`, `undo_id`), a negative path, and that the write shows through the
HTTP API the UI uses. Run them with `dotnet test src/Codex.Mcp.Tests --filter "Category=J4"`.

Where a journey step cannot be met as
written, the class says so: J12 queues the export on Undertow because Product Engineering's copyright risks still block it, and proofs a chapter instead of a Quick preview (pandoc is CI-only); the stdio process has no model key, so J5 offers its attempts with `submit_draft` (generation is
covered in-process by `McpDraftTests`); J3 adds the new book's first chapter and its scene in two batches, since a ref can't
name what the same batch adds; `edit_outline`'s `side_effects` name the renumbering but not what an archive does to a source's clearance
(J4 checks that on the References data). J9 gives Lean Software Development an ISBN first (the seed has none) so the lookup has something to match; J10 path 1 rewrites the Ch 8 words with `submit_draft` and `resolve_draft`, then rewraps the same use onto them as a paraphrase with `update_source_use`; J11 sets DORA's details without the re-check, which goes to the network and is covered in-process by `McpUpdateSourceRecheckTests` over a fake lookup.

## Errors

A failed call is an MCP tool error (`isError: true`). Its text is
`{ "ok": false, "error": { "code", "message", "candidates"?, "field"?, "rule"?, "scope"? } }`.

| Code | When | Extra fields |
|---|---|---|
| `not_found` | No match for a ref, including a number too big for any outline, or a book the user can neither own nor open through sharing | `candidates`: close matches |
| `ambiguous` | A ref matches more than one thing, such as `"Product Engineering"` (it and its `(v1)`, or the user's own book and one shared with them) or a name two entries share | `candidates`; a book or series candidate names its `owner` (`{ id, display_name }`) |
| `out_of_scope` | The entry exists, but not in the book's scope (the book and its series) | `candidates`: the entries found, `scope`: the book and series checked |
| `stale_ref` | stdio only: a display number now names something other than it did at this session's last read | `candidates`: what it was, then what it is now |
| `invalid` | A bad argument. This covers the SDK's own binding failures: a wrong type, a missing required argument, an unknown tool (`field: "name"`) | `field` |
| `conflict` | A stale `expected_version`, or an undo after the prose moved on | `candidates`: the scene |
| `blocked`, `needs_confirmation`, `not_configured`, `rate_limited` | Write tools: `blocked` for an undo already done (`already_undone`), expired (`undo_expired`) or not supported yet (`undo_unsupported`) | `rule` for `blocked`; `open_url` when the author has to act in the UI (`rule: author_decision`) |
| `forbidden` | The token lacks the tool's scope (#32), or the user's role on the book is below what the call needs (#80; *Roles*) | `field`: the scope it needs (none for a role) |
| `unauthorized` | stdio with `CODEX_MCP_TOKEN`: the token stopped working mid-session | |
| `internal_error` | A server fault, with a fixed message. The details go to the log | |

A resource that fails is a protocol error whose message is `"<code>: <message>"`.

## Testing the tools that need a fake model

The stdio child process cannot take a fake `IProseGenerator`, and production has no switch for one. `Codex.Mcp.Tests` has
`McpInProcessServer`: the same wiring as `Codex.Mcp/Program.cs` over the SDK's stream transport inside the test process, with
`FakeProseGenerator` registered, on its own clone of the seed database. It is test-project code only.
