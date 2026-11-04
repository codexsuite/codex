# Built-in layout templates

One folder per template package, named by the slug the seed's `layout_template` rows use. The app reads them from
`CODEX_TEMPLATES_ROOT` (this folder in dev and tests; `/templates` in the web and worker images). The package format
is documented in `src/Codex.Application/Features/Print/Templates/TemplateLoader.cs` (manifest fields in
`TemplateManifest.cs`); the AST the filters receive is described in `ManuscriptAssembler.cs`. Versions, pinning, user
templates (fork, upload, upgrade) and the sandbox every template compile runs in are in [`docs/print.md`](../docs/print.md).
Raise a package's `version` (and add a `changelog` line) when its output changes: the startup sync then offers the new
version to books, which stay pinned to theirs until they apply it.

| Slug | Look | Engine | Raw LaTeX | Citations |
|---|---|---|---|---|
| `fantasy-rpg` | Fantasy RPG (5e style): the Players Guide setup, two columns, rpgtex boxes | Tectonic | kept | footnotes |
| `novel-6x9` | Novel trade paperback (6 x 9, 5.5 x 8.5) on memoir | Tectonic | kept | footnotes |
| `manuscript` | Standard manuscript format (Shunn) for submissions | Tectonic | dropped | author-date |

## Licences

- `fantasy-rpg/tex/` is [rpgtex/DND-5e-LaTeX-Template](https://github.com/rpgtex/DND-5e-LaTeX-Template) v0.8.0,
  MIT (`fantasy-rpg/tex/LICENSE`), vendored unchanged except that `img/paper.jpg` (1.6 MB) is replaced by the
  release's own `img/paper-low-res.jpg` under that name, and the example, build scripts and screenshots are left
  out. The fonts it loads (Bookman, Gillius, Kp-Fonts, Royal initials) come from Tectonic's TeX Live bundle under
  their own free licences; none is vendored here.
- Everything else here (wrappers, Lua filters, preview styles, samples) is part of Codex.
