# Print layout templates (#13)

How Codex lays a book out for print: template packages, their versions, how a book pins one, user templates, and the
sandbox every template compile runs in. The render pipeline itself (assembler, Quick preview, proofs, Export for
printer) is described in the code: `Features/Print/Render/RenderService.cs` and `ManuscriptAssembler.cs`.

## Packages

A template is a versioned **package**: a folder (or a `.zip` of one) holding

| File | What |
|---|---|
| `template.json` | The manifest (`TemplateManifest`): `slug`, `name`, `version` (major.minor.patch), `engine` (`tectonic`), `trims`, `page` (columns, column gap, margins in inches), `citations`, `rawLatex`, `filters`, `options` (the options schema: `boolean` or `choice`, with a default and a label), `directives`, `files`, and an optional `changelog` (one line per change, shown with "v1.1.0 available") |
| `wrapper.tex` | A pandoc template (`$body$`, `$title$` and the metadata the assembler sets) |
| `filters/*.lua` | Pandoc Lua filters, run in the order the manifest lists them |
| `preview.css` | Optional: the Paged.js look of Quick preview. Without it Quick preview uses the house style |
| `tex/` | Classes, styles, fonts and images, copied to the root of each compile's job folder |
| `sample.md` | Pandoc Markdown rendered for the picker's thumbnail |

`TemplateLoader.Load` reads a package and lists its **problems** (anything that stops it compiling: a missing wrapper or
filter, an unknown trim or engine, a bad options schema, a slug that does not match its folder) and **notices** (no
`preview.css`). The built-ins live in the repo's `templates/` (`CODEX_TEMPLATES_ROOT`).

## Versions and pinning

- Every `layout_template_version` row keeps its package as a `.zip` on the row (`package_zip`, with `package_sha256`,
  the package's content hash). Versions are immutable.
- **Built-in sync** (`BuiltInTemplateSync`, at startup): each package under `CODEX_TEMPLATES_ROOT` gets its
  `layout_template` (owner NULL, found by slug) and a published version row for its manifest version. A new version on
  disk adds a row, which `book_layout_current` then offers as the upgrade; the same version with changed files (a fix
  shipped without a bump) repoints its row at the new package. So a book pinned to an older built-in still renders
  after the app ships a newer one.
- **Opening a version** (`TemplateStore.OpenAsync`): a built-in's version that matches the folder on disk renders from
  that folder; any other version is unpacked once from its zip into `CODEX_TEMPLATE_CACHE/<version id>-<hash>/<slug>`
  (zip-safe, see below) and loaded from there.
- **Pinning**: `book_layout_template` holds, per book and template, the pinned version, trim and option values. Picking a
  template the book never used pins its newest published version; picking one it used before restores that pin.
  Rendering, preflight, proofs and exports all resolve the pinned version (`TemplateStore.ResolveAsync`); a queued proof
  or export renders the version recorded on its `export_job` row, even if the book upgrades meanwhile. Cache keys and the
  content hash include the version and the package hash, so a different version never reuses a proof.
- **Upgrading** is explicit: Settings → Layout & print shows "v1.1.0 available" with the changelog, **Preview** renders
  Quick preview with the newer version (`POST /api/books/{id}/layout/preview` with `templateVersionId`), and **Apply**
  (`POST /api/books/{id}/layout/upgrade`) re-pins that book, keeping the trim and options the new version still offers.
  Other books stay on their version.

## User templates

A user's templates belong to them alone: another user's template or version is `not_found`. Built-ins can be forked,
downloaded and previewed by anyone, never changed or archived.

| Route | What |
|---|---|
| `POST /api/layout-template-versions/{id}/fork` | Copies a version into a new template of the user's at 1.0.0, slug `<slug>-fork` (made unique), `forked_from_version_id` set |
| `GET /api/layout-template-versions/{id}/package` | The version's package as a `.zip` |
| `POST /api/layout-templates` | Uploads a `.zip` as a new template (its slug must be free among the user's templates and the built-ins) |
| `POST /api/layout-templates/{id}/versions` | Uploads a `.zip` as a new version of the user's template (same slug, a higher version) |
| `POST /api/layout-templates/{id}/archive` | Hides it from the picker; refused while a book uses it |
| `GET /api/layout-template-versions/{id}/sample` | `sample.md` through the template's filters as html5, with its `preview.css` and page size, for the thumbnail |

The workflow: fork a built-in, download the fork, change its class, wrapper, filters or CSS, raise `version` (and add a
`changelog` line), upload it as a new version, and apply it to the books that should have it. An upload is answered 400
with the loader's problems when the package cannot compile.

**Thumbnails** are drawn in the browser, not stored: the sample's HTML and `preview.css` go into an `<iframe sandbox>`
(no scripts, and a Content-Security-Policy that loads nothing from the network) laid out at the page's width and margins
and scaled down. That needs no image tooling on the server, always matches the version's CSS, and keeps a user's CSS
and HTML from reaching the app. `layout_template.thumbnail_asset_id` stays unused.

## The sandbox (hardening checklist)

User templates are code: Lua filters and TeX run on the server. Built-in and user templates take exactly the same path:
`RenderService` stages the package's wrapper and filters into a fresh job folder, and `PandocRunner` and
`TectonicRunner` run them through `SandboxedProcess` and `src/sandbox/codex-sandbox.c`. Checklist:

- [x] **Uploads are checked before anything is unpacked** (`TemplateArchive.Extract`): no absolute or climbing paths
  (zip slip), backslashes, drive letters or control characters in names; no symbolic links or other non-regular entries;
  no duplicate paths; at most 1000 files, 32 MB per file and 64 MB in all, counted while inflating; the upload itself at
  most 25 MB. Then `TemplateLoader` refuses a package whose wrapper or filters lie outside it.
- [x] **No shell**: Landlock lets the sandboxed program execute only itself (`--exec`), so a Lua `os.execute`,
  `io.popen` or `pandoc.pipe` cannot start a shell or any other program; Tectonic has shell escape (`\write18`) off.
- [x] **No network**: TCP connections are refused (Landlock ABI 4; no ports allowed), so `pandoc.mediabag.fetch` and the
  like fail. Pandoc also runs with `--sandbox`.
- [x] **Files**: read-write only in the per-job temp folder (created 0700, deleted after the job, stale ones swept at
  startup); read-only the warm Tectonic bundle cache and a few system paths (fonts, TLS roots); nothing else is readable,
  so a template cannot read the app's files, other jobs or other users' packages. The unpacked template cache is not
  mounted into the sandbox; the files a job needs are copied into its folder.
- [x] **Limits**: wall clock and CPU time (`CODEX_RENDER_TIMEOUT_SECONDS`; pandoc gets a third), file size
  (`CODEX_RENDER_MAX_PDF_MB`), address space for Tectonic, process count (`CODEX_RENDER_MAX_PROCS`), no new privileges.
- [x] **Environment**: replaced, so no secret (database connection, API keys) reaches a template; arguments are an
  argument list, never a shell string.
- [x] **No fallback**: without Landlock `codex-sandbox` refuses to run (exit 125) and the render fails; Codex never
  compiles unsandboxed.
- [x] **Output**: thumbnails and previews of a user template render in a sandboxed iframe in the browser (above).

Tests: `RenderSafetyTests`, `TectonicSandboxTests` and `TectonicLimitsTests` (built-ins and plain TeX) and
`UserTemplateSandboxTests` (an uploaded template whose Lua tries `os.execute`, `io.popen` and a network fetch to a
listener the test opens, and whose wrapper asks for `\write18`) need pandoc, Tectonic and codex-sandbox, so they run in
CI. `TemplateArchiveTests` (zip safety) and `TemplateLibraryApiTests` (ownership, validation, pinning) run anywhere.

## Settings

| Variable | What |
|---|---|
| `CODEX_TEMPLATES_ROOT` | The built-in packages (repo `templates/`; `/templates` in the images) |
| `CODEX_TEMPLATE_CACHE` | Where stored packages are unpacked (default `<temp>/codex-template-cache`); safe to delete |
| `CODEX_RENDER_WORK` | Parent of the per-job temp folders |
