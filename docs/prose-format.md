# Prose Markdown format

How a scene body (or an inline scene summary) is stored, and how `src/Codex.Client/src/prose/` turns it into a
ProseMirror document and back. The format contract is the twelve files in `db/prose-corpus/` (see its README); the
database triggers that read the same text are in `db/schema/0001_tables.sql`.

## Guarantee

`serialize(parse(x)) === x` byte for byte for every canonical `x`, for both variants. Canonical means: blocks
separated by exactly one blank line, a non-empty body ending in one `\n`, the empty body being zero bytes, LF line
ends, no trailing spaces. Input that is not canonical is normalised (never dropped). `parse` never throws: anything it
does not understand becomes an opaque block that serializes back unchanged.

Tests: `prose.roundtrip.test.ts` (all corpus files, plus a fast-check generator over every construct, 1000 runs per
variant), `prose.parse.test.ts` (`locate`, unknown directives), `prose.readers.test.ts` (the reader rules below, shared with the C# reader).

## API (`src/prose/index.ts`)

| | |
|---|---|
| `parse(markdown, 'prose' \| 'summary') → Node` | ProseMirror document for the variant |
| `serialize(doc) → string` | either variant (told apart by whether the schema has `blockquote`) |
| `locate(doc, {kind, id}) → {from, to}[]` | kinds `beat`, `kbAddition`, `ref`, `mention`, `chapterLink`; document order |
| `proseSchema`, `summarySchema`, `schemaFor(variant)` | schemas built from the same extensions the editors use |
| `ProseEditor`, `SummaryEditor` | React editors (below) |

## Block grammar (variant `prose`)

Blocks are separated by a blank line. A paragraph runs to the next blank line, a `::` line or a ```` ``` ```` line.

| Construct | Markdown | Node |
|---|---|---|
| Paragraph | text; a newline inside it is a soft break | `paragraph`, `hardBreak` (the break is a soft one; Shift+Enter inserts it) |
| Heading | `## text` (1 to 6 `#`, one space) | `heading{level}` |
| Block quote | `> ` before each line (`>` for a blank one) | `blockquote` |
| Bullet list | `- item`; content of an item is indented 2 | `bulletList > listItem` |
| Ordered list | `1. item`; the first number is the start, the rest count up | `orderedList{start} > listItem` |
| Nested list | a list inside an item, indented by the item's marker width (2 under `- `, 3 under `1. `), with no blank line between items | `listItem > paragraph, bulletList \| orderedList` |
| Table | GFM pipe table: a header row, a delimiter row (`---`, `:---`, `:---:`, `---:` per column), then body rows; every row starts and ends with `\|`; `\|` inside a cell is `\\|` | `table{align[], source} > tableRow > tableHeader \| tableCell > paragraph` (see *Tables*) |
| Horizontal rule | `---` alone on a line, as its own block (`***` and `___` read the same and are written `---`) | `horizontalRule` |
| Leaf directive | `::beat{id="…"}`, `::kb-addition{id="…"}`, `::figure{id="…"}`, `::spell{kb="…"}`, `::statblock{kb="…"}`, `::pagebreak` | `directive{name, ref}` (atom) |
| Container | `:::readaloud`, `:::sidebar{title="…"}`, `:::aside`, blocks, then a bare `:::` | `container{name, title}`; not nested |
| Raw LaTeX | ```` ```{=latex} ```` … ```` ``` ```` | `rawLatex{source}` (atom) |
| HTML comment | `<!--` at the start of a block, through the line that holds `-->` | `opaqueBlock{raw}` (kept, never printed; print flags text after the `-->` on its line and a comment that never closes, since both hide author text) |
| Anything else | an unknown `::name…` or `:::name…` (through its `:::`), any other fenced block, an attribute set the parser does not know, an unclosed container or fence (to the end) | `opaqueBlock{raw}` (atom) |

### Tables (#64)

- A table starts a block (after a blank line, or at the start of the body or of a container) and runs while lines
  start with `|`. Its second line must be a delimiter row with the header's column count; otherwise the lines are a
  paragraph. Body rows with fewer cells are padded with empty cells and extra cells are kept as extra columns, so
  `parse` still never throws.
- Cells hold inline content only (the inline grammar below, mentions and `ref:` marks included): one `paragraph` per
  cell, never a hard break or a block. That is what lets every table go back to a pipe table.
- `align` is one entry per column: `null`, `"left"`, `"center"` or `"right"`.
- **Lossless:** `source` holds the table's text as read. While the table's content still equals `parse(source)`, the
  serializer writes `source` back byte for byte (hand-padded columns survive a round trip). Once a cell, row, column
  or alignment changes, the table is written in canonical form: `| ` + cell + padding + ` |` per cell, each column
  padded to its widest cell (width in UTF-16 code units), delimiter cells of `-` filling the column width + 2 with
  `:` at the aligned ends, and `source` is cleared. A table built in the editor has no `source` and is canonical from
  the start.
- The first line of a paragraph that starts with `|` is escaped (`\|`) so it does not read back as a table; a first
  line that is exactly `---` is escaped the same way (`\---`; `***` and `___` are already escaped by the `*` and `_`
  rule), and so is one that starts with `<!--` (`\<!--`). Inside a list item, any paragraph line that looks like a
  list marker is escaped too, because a marker line there starts a nested list.
- A column is at least 1 wide, so an empty column still has a valid delimiter cell (`---`, `:--`, `--:`, `:-:`).
- A cell is split at every `|` not preceded by a backslash; `\|` is read as `|` before the cell's inline parse, and
  written for every `|` in the cell text (code spans included). Spaces and tabs around a cell are dropped.

### Raw LaTeX inline and blocks (#64)

- ```` ```{=latex} ```` blocks stay `rawLatex{source}`; they become editable in the rich view (CodeMirror), not a new
  node. A raw inline span is `rawLatexInline{source}` and serializes with the shortest backtick fence that fits, as
  `code` does.
- Both stay flagged "template-specific" in print: LaTeX templates pass them through (`RawBlock` / `RawInline
  "latex"`); other outputs drop them and flag them. Quick preview draws a labelled placeholder; Proof renders them.
- Math (`$…$`, `$$…$$`) is **not** part of the format: `$` is plain text.

Lists, quotes and containers hold ordinary blocks; an empty item, quote or container gets one empty paragraph in the
document and serializes without it. Empty paragraphs and headings are not written. An empty document is one empty
paragraph.

## Inline grammar

| Construct | Markdown | Node / mark |
|---|---|---|
| Emphasis, strong | `*x*`, `**x**`, `***x***` | marks `italic`, `bold` |
| Code | `` `x` `` (a longer fence if the text holds backticks; one space of padding on each side when the text starts or ends with a backtick, or starts and ends with a space and is not all spaces; the reader strips that one space, as CommonMark does) | mark `code`; unlike TipTap's stock mark it combines with others |
| Mention | `[Name](kb:<entry id>)` | inline atom `mention{id, label}` (an atom, not a mark: the label always names one entry and cannot drift from it) |
| Chapter link | `[Title](chapter:<chapter id>)` | mark `chapterLink{id}` |
| Source passage | `[passage](ref:<use id>)`; may contain and be contained by other marks | mark `ref{id}` |
| Bare source marker | `[^](ref:<use id>)` | inline atom `refMarker{id}` |
| Strikethrough | `~~x~~` (same flanking rule as emphasis; only a run of exactly two `~` is a delimiter) | mark `strike` |
| External link | `[text](https://…)`, `http://…` or `mailto:…` followed by non-space, non-`)` characters; any other target stays text. The serializer percent-encodes whitespace, `(`, `)`, `<` and `>` in a target, and writes only the text of a link whose target is not one of these | mark `link{href}` |
| Inline raw LaTeX | `` `\DndDropCapLine{F}`{=latex} ``: a code span followed directly by `{=latex}` | inline atom `rawLatexInline{source}` (a chip) |
| Fence break | `<!---->` between a closing and an opening backtick fence (`` `x`<!---->`\y`{=latex} ``) | nothing; the serializer writes it where a code span is followed directly by a chip, because two fences side by side read as one longer fence. A literal `<!---->` in text is written `\<!---->` |
| Escapes | `\` before `\ * _ ` [ ]` (and, at a line start, before a character that would begin a block) ; also before `~` where two would read as `~~`, and before `\|` in a table cell or at the start of a paragraph line (new in #64, so existing bodies stay canonical) | plain text |

Notes for anyone writing a second reader (the C# helpers do):

- Emphasis follows CommonMark's delimiter algorithm with a whitespace-only flanking rule. `_` and `__` are read the
  same way; the serializer writes them only to keep two touching runs apart (`*a*__b__`), and always escapes a literal
  `_`.
- A mark never starts or ends on whitespace; the serializer moves it outside the delimiters.
- The serializer picks the first delimiter strategy whose output parses back to the same content, and re-serializes
  from its own output until it stops changing, so the result is a fixed point of `parse` then `serialize`.
- Variant `summary` is one paragraph of text, `bold`, `italic`, `code` and `mention`; a newline stays in the text and
  `ref:` or `chapter:` links stay literal. Its Markdown has no trailing newline.

## Reader rules

Two readers parse this format: `src/prose` (TypeScript, the editors) and `Codex.Application.Prose.ProseDocument`
(C#, server-side edits through `IProseStore`). They agree on these rules for any input, canonical or not;
`src/Codex.TestSupport/Fixtures/prose/reader-cases.json` pins them (top-level blocks and the embeds found), plus the
embeds of every corpus file, and both `prose.readers.test.ts` and `ProseReaderCaseTests` read it.

| Case | Rule |
|---|---|
| Ids | An embed id is a UUID (8-4-4-4-12 hex, either case), as the triggers require. `[x](kb:goodhart)` is text; `::beat{id="b1"}` is an opaque block. |
| `::` lines | Any line starting `::` (not `:::`) ends a paragraph. A known directive with a valid id is a directive; anything else, including a trailing space, is an opaque block. |
| Fences | A line starting ```` ``` ```` opens a fence (also inside a paragraph); only a line that is exactly ```` ``` ```` closes it. Unclosed, it runs to the last non-blank line. |
| Headings | `#{1,6}`, one space, text; one line only. `#tag` and `#######` are paragraph text. |
| Quotes, lists | A quote is its run of `>` lines. A list runs while lines are items of the same kind (`-` or `1.`) or are indented under the current item; a bullet line after an ordered list starts a new list. |
| Paragraphs | Run to a blank, `::` or ```` ``` ```` line and absorb `#`, `>` and `-` lines. |
| Containers | `:::readaloud`, `:::aside` or `:::sidebar{title="…"}` with a later bare `:::` hold ordinary blocks. Any other name, a `title` on a non-sidebar, no closing line, or a stray `:::` make an opaque block (embeds inside are not read). |
| Tables, rules, comments (#64) | A table needs a `\|`-led header line followed by a delimiter row with the same cell count; its block runs while lines start with `\|`. A rule is a block that is exactly `---`, `***` or `___` (under text it is text). An HTML comment starts a block with `<!--` and runs through the line holding `-->` (unclosed: to the last non-blank line); it is opaque and its embeds are not read. |
| Inline (#64) | `~~` is a strike delimiter only as a run of exactly two `~`. An external link target is `http://`, `https://` or `mailto:` followed by non-space, non-`)` characters. `{=latex}` must follow the closing backtick directly. A code span closes at a run of backticks as long as the opening run; one padding space on each side is dropped (not when the text is only spaces). |
| Link text | Holds no unescaped `[` or `]` (the triggers' regex), so `[a [b] c](kb:…)` is text. A ref mark therefore never wraps a mention: the serializer would write nested brackets the triggers ignore. |

Known differences, both outside what the editor writes: the triggers also accept a directive line with trailing
spaces or tabs, and read `ref:` links and `::` lines inside fences and opaque blocks; the TypeScript reader also
reads a directive inside a quote or list item, where the C# reader and the triggers see quote or list text.

## Editors

```tsx
<ProseEditor
  value={markdown}                 // replaces the content when it differs from the last onChange output
  onChange={(markdown) => …}
  entries={[{ id, name, category, aliases }]}   // the @ picker; Enter inserts [Name](kb:id)
  slashItems={items}               // the / menu
  readOnly
  placeholder="…"
  renderDirective={({ name, id }) => <MyCard/>} // optional card for a leaf directive
  onEditor={(editor) => …}         // the TipTap editor, for locate() + scrolling
/>
<SummaryEditor value onChange entries readOnly placeholder />
```

### Adding a slash item

A lane builds its own list and passes it as `slashItems` (the editor keeps no global registry):

```ts
const items: SlashItem[] = [
  {
    id: 'scene-beat', label: 'Scene beat', group: 'AI',
    enabled: (editor) => !editor.isActive('container'),   // or a plain boolean
    run: (editor) => editor.chain().focus().insertContent({ type: 'directive', attrs: { name: 'beat', ref: newId } }).run(),
  },
];
```

Typing `/` after a space opens the menu at the caret; the query filters by label; arrows and Enter pick. Choosing an
item deletes the typed `/query`, then calls `run`. A disabled item is dimmed (`aria-disabled`) and neither deletes
nor runs. Items appear in the order given, with a heading each time `group` changes.

`richBlockSlashItems()` (exported from `prose`) returns the two items every prose surface can use: **Table** (a header
row and two rows of two empty cells, caret in the first header cell) and **Divider** (a `---` rule). Write's list
includes them. While the caret is in a table a toolbar floats over it: row above or below, column left or right, delete
row, column or table, and column alignment (default, left, centre, right). The header row (the first) takes no row above
and is not deleted while body rows remain. Editing a table clears its `source`, so it is written as the canonical pipe
table. A cell holds one paragraph: Enter moves to the next cell, Shift-Enter does nothing, and pasted blocks become
plain text. Raw LaTeX blocks are a small CodeMirror editor in the document; an inline raw LaTeX chip is edited by
clicking it (or selecting it and pressing Enter), and emptying it removes it.

### Adding a card renderer

Leaf directives, containers, raw LaTeX and opaque blocks are non-editable cards (a container's contents stay
editable). Pass `renderDirective` to draw a leaf directive yourself; return `undefined` to keep the default card:

```tsx
renderDirective={({ name, id }) => (name === 'beat' ? <BeatCard beatId={id!} /> : undefined)}
```

The card sits inside a `contentEditable={false}` wrapper (class `prose-card`) with `data-testid="prose-directive-<name>"`, `data-directive="<name>"` (the styling hook)
and `data-ref="<id>"`. Directive nodes are draggable and selectable; moving one is an ordinary ProseMirror transaction.

### Test ids

`src/prose/testIds.ts` (`proseTestIds`, prefix `prose-`): `editor`, `summaryEditor`, `mentionPicker`,
`mentionOption(id)`, `slashMenu`, `slashItem(id)`, `directiveCard(name)`, `containerCard(name)`, `rawLatexCard`,
`opaqueCard`, `rawLatexInline` (the chip for an inline raw LaTeX span). The editors' contenteditable carries `editor` / `summaryEditor` itself.

## Editor save guard (#64)

`PUT /api/scenes/{id}/prose` (`EditorProseSave`, rules in `Codex.Application.Prose.EditorProseRules`) checks every body, Rich
or Source, inside the store's transaction. It reads ids the way the triggers do (`McpProseRules.IdsIn`: a `::beat`,
`::kb-addition` or `::figure` directive id or a `ref:` link id anywhere in the text, fences and raw LaTeX included). An id
is refused (400 `validation`, web wording) when it is not in the stored body and is not a row of this scene
(`beat`, `kb_progression`, `figure`, `reference_use`, archived or not). The scene's own archived rows are allowed on
purpose: cutting a `::beat` line in Source, an autosave, and pasting it back is legitimate, and undo does the same. A
random id, or one from another scene, is refused; the database triggers (`prose_check_embeds`) also reject a foreign
anchored directive or `ref:` id, but with a database message, and they ignore a directive that is not at the start of a
line. The check reads no client flag, so a refused draft that is kept and saved again later is checked the same way.
MCP keeps its stricter rule:
an agent may not add any id (`docs/mcp.md`).

## Files

`schema/` (node and mark specs, schemas) · `markdown/` (`inlineParse`, `parse`, `serialize`) · `locate.ts` ·
`ProseEditor.tsx`, `SummaryEditor.tsx`, `useProseEditor.ts`, `editorExtensions.ts` · `MentionPicker.tsx`,
`SlashMenu.tsx`, `suggestion/` · `views/` (cards) · `src/test-support/proseGenerators.ts` (fast-check generators).
