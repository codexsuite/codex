#!/usr/bin/env bash
# Installs the print toolchain the render core runs (pandoc, Tectonic, codex-sandbox) into one folder, at the versions
# src/Dockerfile.worker pins. CI's API-test job uses it; on the agent host the
# folder is ~/.local/codex-tools/bin (on PATH via codex-env.sh). codex-sandbox is built with the host's cc, or, where
# there is none (a minimal runner image), inside a pinned gcc container. With a second argument it then warms that
# Tectonic cache (scripts/warm-tectonic-cache.sh), so no test waits on a bundle fetch.
#   scripts/install-print-tools.sh <bin-dir> [<tectonic-cache-dir>]
set -euo pipefail

PANDOC_VERSION=3.12
PANDOC_SHA256=67d7d011fed8c8543306022b985b9b2499ab9b74818df91d8727c7e9ebc5ba06
TECTONIC_VERSION=0.15.0
TECTONIC_SHA256=dfb82876f2986862996e564fa507a9e576e0c1e3bee63c2c1bd677c2543e6407

bin="${1:?usage: install-print-tools.sh <bin-dir>}"
repo="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
work="$(mktemp -d)"
trap 'rm -rf "$work"' EXIT
mkdir -p "$bin"

curl -fsSL -o "$work/pandoc.tgz" "https://github.com/jgm/pandoc/releases/download/${PANDOC_VERSION}/pandoc-${PANDOC_VERSION}-linux-amd64.tar.gz"
echo "${PANDOC_SHA256}  $work/pandoc.tgz" | sha256sum -c -
tar -xzf "$work/pandoc.tgz" -C "$work"
install -m 0755 "$work/pandoc-${PANDOC_VERSION}/bin/pandoc" "$bin/pandoc"

curl -fsSL -o "$work/tectonic.tgz" "https://github.com/tectonic-typesetting/tectonic/releases/download/tectonic%40${TECTONIC_VERSION}/tectonic-${TECTONIC_VERSION}-x86_64-unknown-linux-musl.tar.gz"
echo "${TECTONIC_SHA256}  $work/tectonic.tgz" | sha256sum -c -
tar -xzf "$work/tectonic.tgz" -C "$work"
install -m 0755 "$work/tectonic" "$bin/tectonic"

GCC_IMAGE=gcc:14.2.0
if command -v cc > /dev/null; then
  cc -O2 -static -Wall -Wextra -o "$bin/codex-sandbox" "$repo/src/sandbox/codex-sandbox.c"
else
  # Source in on stdin and the binary out on stdout, so it works whatever filesystem the Docker daemon sees (DinD).
  docker run --rm -i "$GCC_IMAGE" sh -c 'cat > /tmp/s.c && gcc -O2 -static -Wall -Wextra -o /tmp/s /tmp/s.c >&2 && cat /tmp/s' \
    < "$repo/src/sandbox/codex-sandbox.c" > "$work/codex-sandbox"
  install -m 0755 "$work/codex-sandbox" "$bin/codex-sandbox"
fi

"$bin/pandoc" --version | head -1
"$bin/tectonic" --version

if [ -n "${2:-}" ]; then
  PATH="$bin:$PATH" "$repo/scripts/warm-tectonic-cache.sh" "$2"
fi
