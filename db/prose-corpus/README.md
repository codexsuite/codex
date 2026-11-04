# Prose corpus

Fifteen scene bodies in the stored prose format (issue #10, *Prose text format*). They are the round-trip contract
for the TypeScript editor module and the C# prose helpers.

Rules:

- `serialize(parse(x))` must equal `x` byte for byte, for every file here.
- These files are frozen. New cases go in generated property tests, not here. Files 13 to 15 were added by #64,
  which grew the format (tables, horizontal rules, strikethrough, external links, nested lists, HTML comments,
  inline raw LaTeX).
- Canonical form: blocks are separated by exactly one blank line; a non-empty body ends with exactly one `\n`;
  the empty body is zero bytes. UTF-8, LF, no trailing spaces.
- The `words` column is `markdown_word_count(body)` from `db/schema/0001_tables.sql`. `ProseCorpusTests` parses this
  table and checks the database agrees, so the format stays pinned to the triggers.

| file | words | pins |
|---|---|---|
| `01-paragraphs-and-marks.md` | 75 | paragraphs, a soft break inside a paragraph, emphasis, strong, both, inline code, an escaped `*` |
| `02-beats.md` | 35 | `::beat{id="…"}` lines between paragraphs, including two in a row |
| `03-kb-addition.md` | 44 | `::kb-addition{id="…"}` as a top-level line, with a `kb:` mention after it |
| `04-mentions-and-links.md` | 56 | `[Name](kb:<uuid>)` beside punctuation, inside emphasis and strong; `[Title](chapter:<uuid>)` links |
| `05-ref-marks.md` | 97 | `[passage](ref:<use uuid>)` marks: one spanning emphasis and strong, a quote repeated over two paragraphs, a bare `[^](ref:…)`, escaped brackets in link text |
| `06-quotes-lists-headings.md` | 62 | ATX headings, block quotes (one with two paragraphs), bullet and ordered lists |
| `07-print-directives.md` | 13 | `::figure{id}`, `::spell{kb}`, `::statblock{kb}` (a spell repeated), `::pagebreak` |
| `08-containers.md` | 62 | `:::readaloud`, `:::sidebar{title="…"}` holding a list, `:::aside`, each closed by `:::` |
| `09-raw-latex.md` | 15 | a fenced ```` ```{=latex} ```` block, which the word count ignores |
| `10-unicode.md` | 64 | curly single and double quotes, em and en dashes (glued and spaced), an ellipsis, a non-breaking space and a thin space |
| `11-edge-cases.md` | 30 | a body that starts and ends with directive lines; directive text inside a code span is not a directive |
| `12-empty.md` | 0 | the empty scene |
| `13-tables.md` | 61 | pipe tables: alignment, marks, a mention and a `ref:` mark in cells, a hand-padded table kept byte for byte, an escaped `\|` in a cell, an empty cell |
| `14-more-markdown.md` | 51 | a horizontal rule, strikethrough, `https:` and `mailto:` links, bullet and ordered lists nested both ways, an HTML comment |
| `15-inline-latex.md` | 28 | raw LaTeX inline spans (one gluing a drop cap to its word) and a ```` ```{=latex} ```` block |

## Syntax choices where #10 was silent

- Headings are ATX (`##`, `###`) with no trailing `#`s. Scene prose rarely uses them; they are pinned so the editor
  neither drops nor rewrites them.
- Directive attributes are double-quoted with a single space-free form: `{id="…"}`, `{kb="…"}`, `{title="…"}`. A
  `::figure` carries only `id` (the figure row owns placement, per #10).
- A directive occupies a whole line with no trailing space, and every block is separated by a blank line, even though
  the triggers would also accept a directive line directly under text.
- Container content is ordinary blocks (paragraphs, lists) between the opening line and a bare `:::`. Containers are
  not nested here.
- A bare reference marker is `[^](ref:<use uuid>)`. Brackets inside link text are backslash-escaped (`\[`, `\]`), the
  form the reference regex expects.
- Lists use `-` for bullets and `1.` style for ordered lists. Hard line breaks are not part of the format; tables are
  GFM pipe tables (#64).
- Ids come from `db/seed/ids.csv` where a seed row exists (for example `kb:` Goodhart's Law, `chapter:` Ch 5, `ref:`
  Beck's use). The `ref:` ids in `05-ref-marks.md` share one use per scene in real data; here they only need to be
  well-formed.
- No accented letters, CJK or emoji in the corpus: `markdown_word_count` uses `[[:alnum:]]`, so its result for them
  depends on the database locale. Test databases pin `en_US.UTF-8` (`CodexDatabase.Locale`, the `postgres:18`
  image's default), but the corpus stays locale-independent anyway. Round-tripping such text belongs in the
  generated property tests.
