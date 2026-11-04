# Import

Library → **Import** (`/?import=1`) turns files into a new book in one transaction: parse (`IImportParser` for the
source) → preview (`POST /api/imports/preview`, nothing written) → import (`POST /api/imports`, `ImportWriter`).
Undo import deletes the book (soft: `book.deleted_at`, hidden everywhere) while nothing has been written in it since. Code: `Codex.Application/Features/Import`,
endpoints in `Codex.WebApi/Features/Import/ImportsController.cs`, the dialog in `Codex.Client/src/features/library`.

| Source | Upload | Reads |
|---|---|---|
| `markdown` | a folder | issue #10's `act-NN-*/chapter-NN-*/scene-NN.md` layout (`MarkdownFolderParser`) |
| `novelcrafter` | one `.zip` | a Novelcrafter export (`NovelcrafterZipParser`) |
| `docx` | one `.docx` | Heading 1 chapters, Heading 2 scenes (`DocxParser`) |
| `pandoc` | a folder, with images | a pandoc book folder: Markdown with raw LaTeX for the rpgtex dndbook class (`PandocFolderParser`, below) |

Limits (`ImportLimits`): 2000 files, 25 MB of text, 2 MB per text file (larger ones are skipped). Every path must be
relative to the picked folder. Every file the importer does not read is listed as skipped with a reason, in the
preview and on the new book's banner.

## Codex lists in a Markdown folder (`markdown`)

A scene's or chapter's `codex:` front matter lists names, or mappings with `name`, `path` (the entry's file) and `url`
(`MarkdownCodexEntries`). A `path` is looked up among the uploaded files only, relative to the referencing file, then
the manuscript folder, then the folder that holds it; one that matches is the entry's body, and its name, summary,
aliases, `alwaysIncludeInContext` and Novelcrafter id are kept. The category is the file's first tag, else its `type`,
else its kind folder (`characters/` ...), else `Imported`. The `url` goes to the entry's `import_url`. Entries are keyed
by the resolved path (case-insensitive), else by lower-cased name; entries that end up with one name in one category
are told apart as `Name (Chapter title)` of the chapter that first names them. A chapter's list creates its entries but
links no scene (no chapter-to-entry table, and linking every scene would flood them). A path with no matching file is
counted in one skipped note and the entry keeps its name only.

## Pandoc book folder (`pandoc`)

The shape of a campaign book's folder (#13 part 1): `Chapter 1.md` … `Chapter 10.md`, the order in
`PlayersGuide.yaml`, images under `images/…`, raw LaTeX throughout.

### Upload

The dialog sends the `.md` and `.yaml` files and only names the rest. The preview answers with `images`: the paths of
the images the chapters place. Import book then sends those images as well (and only those: the real folder has
1.8 GB of art plus Stable Diffusion batches). An image over 25 MB is named as too large instead. The endpoint never
reads the images into memory: the form reader spools them to disk, and the asset intake reads each once. Caps: 25 MB
and 64 MP per image (the asset upload's), 4 GB of images per import.

Images become assets through `IAssetIntake` (`Codex.Contracts.Print`), the same checks, storage and rendition job as
`IAssetService.UploadAsync`. They are stored under the new book's id before its transaction, and their rows are
written in it, so the book, its images, figures and openers commit together. An image the intake refuses (not an
image, too large) is left out with its figure, and listed with the reason. The book's layout is chosen after the
commit through `IBookLayoutChoice` (the Layout view's own `SelectAsync`).

### Order and structure

- **Order:** the `input-files` of a pandoc defaults `.yaml` (the one nearest the top). Without one, top-level files
  named `Chapter N.md`, in number order. Other files are skipped ("Not one of the chapters PlayersGuide.yaml lists"),
  as is a listed file that is missing. The book's title, subtitle and author come from the YAML's `metadata`.
- **Acts:** each file is an act. Its `\part{X}` opener (the `\begin{figure}[p] … \part{X} … \includegraphics{…}`
  block) names the act X, and its image becomes the act's opener art (`opener_asset_id`, with the `\caption` if there
  is one). A later `\part` in the same file starts another act. A file with no `\part` is an act named after the file.
- **Chapters:** each `# 3.1 Fighter` heading is a chapter "Fighter" with one scene holding the text up to the next
  `#`. Hand numbering (`3.1 `, `3.1.1 `, `2. `) is stripped from every heading level; `##` and deeper stay headings
  in the prose, one level up (`##` is the first level under a chapter).
- **Text before a file's first `#`** (after the part opener) opens that file's first chapter, so nothing is lost and
  the order is kept. In the Players Guide that is only the HTML-comment cheat sheet. A file with no `#` at all is one
  chapter named after its act.

### Mapping

| Source | Stored |
|---|---|
| `\DndDropCapLine{F}ighters` | `Fighters`. The book's layout becomes Fantasy RPG with its `drop-caps` option on (the template draws drop caps). Any other `\Dnd…` command picks Fantasy RPG without setting the option. |
| `\onecolumn`, `\twocolumn` | dropped |
| `\newpage`, `\clearpage` | `::pagebreak`, but only for a manual break: the ones around a part opener, at either end of a file, or straight after another break are dropped |
| `\begin{DndReadAloud}` | `:::readaloud` … `:::` |
| `\begin{DndSidebar}{Title}` | `:::sidebar{title="Title"}` (a `"` in the title becomes `'`) |
| `\begin{DndComment}{Title}` | `:::aside` with `**Title**` as its first paragraph (the prose format gives `aside` no title) |
| `\DndSpellHeader{name}{school}{time}{range}{components}{duration}` | a knowledge base entry in a **Spells** category (fields `level-school`, `casting-time`, `range`, `components`, `duration`, which the Fantasy RPG filter reads) plus `::spell{kb="<entry id>"}`. The text after the header, up to the next header, heading or the end of its `{=latex}` fence, is the entry's body. |
| `![cap](images/…){ width=… }` | the image as an asset, a `figure` row (placement `column`, caption `cap`) and its `::figure{id="…"}` line. The path is URL-decoded and matched exactly, else ignoring case. |
| `\begin{figure}` with one `\includegraphics` and no `\part` | a figure: `[p]` → `page`, `figure*` → `float-top`, else `column`; caption from `\caption` |
| missing image | listed as skipped with its path; the figure (or the opener art) is left out |
| pipe table | kept as written, as its own block (cells keep their Markdown) |
| `<!-- … -->` | kept whole in an opaque `:::comment` … `:::` block: both prose readers keep it, the editor shows it as an unrecognised block, and print leaves it out (flagged as an unknown directive) |
| a `{=latex}` fence holding `\DndSpellHeader` | read like the rest of the chapter, so its spells map; any other `{=latex}` fence stays as it is |
| any other LaTeX | its whole paragraph or environment wrapped in a ```` ```{=latex} ```` fence (template-specific), never left as paragraph text |

Everything else goes through `ImportProse` like the other sources (canonical prose, no text becomes a directive).
The blocks the mapping writes itself (directive lines, tables, fences, comments) are joined around that output, never
escaped by it, and neutralised so that imported text still cannot become an embed.

Tests: `PandocFolderParserTests` and `PandocChapterReaderTests` (unit), `PandocImportTests` and
`PandocImportImageTests` (API), `ImportPandoc.spec.tsx` (dialog), `importProse.test.ts` (the stored chapters are
canonical in the TypeScript reader), and `PlayersGuideRoundTripTests` (import and export). The fixture is
`src/Codex.TestSupport/Fixtures/import/players-guide-campaign/`: a made-up folder in this shape (its README says how).
