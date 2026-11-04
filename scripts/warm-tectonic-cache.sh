#!/usr/bin/env bash
# Fills Tectonic's bundle cache with everything the built-in templates use, so render jobs can run with the cache
# read-only and --only-cached (no network inside the sandbox). Runs pandoc and Tectonic directly, outside the sandbox:
# the inputs are this repository's own templates and samples. Needs pandoc and tectonic on PATH. A cold cache takes
# a couple of minutes (the bundle index and every font); a warm one a few seconds.
#   scripts/warm-tectonic-cache.sh <cache-dir>
set -euo pipefail

cache="${1:?usage: warm-tectonic-cache.sh <cache-dir>}"
repo="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
# In the worker image the script sits in /scripts, so repo is "/": strip it, or "//templates" reads as a URL to pandoc.
repo="${repo%/}"
work="$(mktemp -d)"
trap 'rm -rf "$work"' EXIT
mkdir -p "$cache"
export TECTONIC_CACHE_DIR="$cache"

compile() {
  # The first fetch of the bundle is slow and the network can blip: one retry.
  (cd "$1" && { timeout 900 tectonic --keep-logs --chatter minimal main.tex || timeout 900 tectonic --keep-logs --chatter minimal main.tex; })
}

mkdir -p "$work/plain"
printf '\\documentclass{article}\n\\begin{document}\nHello.\n\\end{document}\n' > "$work/plain/main.tex"
compile "$work/plain"

for template in "$repo"/templates/*/; do
  template="${template%/}"
  [ -f "$template/template.json" ] || continue
  name="$(basename "$template")"
  job="$work/$name"
  mkdir -p "$job"
  if [ -d "$template/tex" ]; then cp -r "$template/tex/." "$job/"; fi
  filters=()
  for filter in "$template"/filters/*.lua; do filters+=(--lua-filter "$filter"); done
  # The metadata ManuscriptAssembler sets, at generic values.
  to_tex() {
    pandoc "$1" --from markdown --to latex --top-level-division=part --syntax-highlighting=none \
      --template "$template/wrapper.tex" "${filters[@]}" \
      -M papersize=letterpaper -M page-width=6 -M page-height=9 -M margin-top=0.75 -M margin-bottom=0.75 \
      -M margin-inner=0.75 -M margin-outer=0.75 -M column-gap=0.3 -M columns=2 -M background=full -M font=courier \
      -M title-page=true -M toc=true -M drop-caps=true --output "$2"
  }
  to_tex "$template/sample.md" "$job/main.tex"
  compile "$job"
  # The sample uses only some of the fonts a book can reach: a bold small-caps heading or a TS1 symbol in bold stopped
  # a Players Guide export (only-cached). Load every series and shape of the wrapper's roman, sans and typewriter
  # families, with TS1 symbols in each, so their font files are cached too. A template that drops raw LaTeX skips it.
  probe="$work/$name-fonts"
  mkdir -p "$probe"
  if [ -d "$template/tex" ]; then cp -r "$template/tex/." "$probe/"; fi
  cat > "$probe/fonts.md" <<'PROBE'
```{=latex}
\makeatletter
\def\codex@probe{Aa \textquotesingle\ \textquotedbl\ \textdegree\ \textonehalf\ \texttimes\par}
\@for\codex@f:=rm,sf,tt\do{\@for\codex@s:=m,b\do{\@for\codex@h:=n,it,sl,sc\do{%
  {\csname\codex@f family\endcsname\fontseries{\codex@s}\fontshape{\codex@h}\selectfont\codex@probe}}}}
\makeatother
```
PROBE
  to_tex "$probe/fonts.md" "$probe/main.tex"
  (cd "$probe" && timeout 900 tectonic -Z continue-on-errors --chatter minimal main.tex >/dev/null 2>&1) \
    || echo "note: $name's font probe reported errors (a shape the bundle lacks); the fonts it found are cached"
  echo "warmed $name"
done
