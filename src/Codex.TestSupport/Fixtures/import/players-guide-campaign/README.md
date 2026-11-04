A made-up campaign folder, *Players Guide to The Realm of Velmara*, in the shape the pandoc import source reads
(`PandocFolderParser`, docs/import.md). Apart from the lines tests quote, its words are random, so most of it reads as
nonsense on purpose; the markup is what matters (CRLF line ends included).

- `PlayersGuide.yaml`: a pandoc defaults file with `input-files` listing the three chapters below (and an `#- index.md`
  comment).
- `Chapter 1.md`: the part opener, an HTML-comment cheat sheet, drop caps, `![…](…)` figures, a `figure*[t]` with a
  caption, a `figure*` without one, an inline comment before an image, and `\onecolumn` in mid-text.
- `Chapter 3.md`: the part opener, the Fighter section with its pipe table, a comment, a figure, `### Mage Hunter`, and
  the `\clearpage` before `# 3.2 Ranger`.
- `Chapter 4.md`: the part opener and the `{=latex}` fence holding two `\DndSpellHeader` spells (one with a numbered
  list).
- `index.md`, `Build.ps1`: files the import lists as skipped.
- `images/`: generated abstract PNGs, 160 px wide: the eight the chapters place, plus one they do not
  (`Chapter 02 Humans.png`).

`players-guide-campaign.expected/` holds the stored prose of each chapter, with ids replaced by
`00000000-0000-4000-8000-00000000000N` in order of appearance (`PandocFolderParserTests` compares against it, and
`importProse.test.ts` checks it is canonical in the TypeScript reader).
