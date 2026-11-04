# Players Guide fixture

A made-up ten-chapter book, used to test the LaTeX import mapping (#64, `LatexImportMapper`) and the editor against text
at the scale and in the shape of a real campaign book (pandoc `markdown+smart+raw_tex`, the rpgtex `dndbook` class):
about 137k words, 522 pipe-table lines, raw LaTeX in every chapter.

The words are random, so the text reads as nonsense on purpose. The markup is what matters here: its tables, comments,
figures, fences and line ends follow a real book's, because those are what tests need to hold on to.

- `source/chapter-NN.md`: the chapters as an author would write them (CRLF line ends). Frozen.
- `mapped/chapter-NN.md`: what `LatexImportMapper.Map` makes of each source chapter: canonical prose (LF, blocks
  separated by one blank line, a final `\n`). Other tests read these as long prose: the TypeScript round trip, the C#
  reader and the Write source toggle.

`PlayersGuideFixtureTests` fails when `mapped/` differs from the mapper's output. After changing the mapper on
purpose, regenerate and review the diff:

```
CODEX_REGEN_PLAYERS_GUIDE=1 dotnet test src/Codex.UnitTest --filter "FullyQualifiedName~PlayersGuideFixtureTests"
```

What the mapping does to this text: most of the book's LaTeX sits inside HTML comments (parked drafts), which pass
through untouched, as do the pipe tables. Outside comments: drop caps are joined to their word, `\newpage` /
`\clearpage` become `::pagebreak` (adjacent ones collapse), `\onecolumn` / `\twocolumn` go, hand-numbered headings lose
their numbers, and every `figure` environment (full-page chapter openers with `\part`, wide images) is kept as a
`{=latex}` fence until assets are imported (#13, #65). Markdown images (`![alt](images/…)`) are not LaTeX; `ImportProse`
keeps them as escaped text. `\DndSpellHeader` appears only inside a `{=latex}` fence (chapter 4, eight spells with their
text), so it stays as it is.
