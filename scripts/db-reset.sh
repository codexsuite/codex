#!/usr/bin/env bash
# Drops and recreates a dev database from db/ (schema -> migrations -> seed, each in file-name order).
# Usage: scripts/db-reset.sh [database-name]
# Server: PGHOST/PGPORT/PGUSER/PGPASSWORD as usual (defaults: localhost:5432, postgres/postgres).
set -euo pipefail

db="${1:-codex}"
export PGHOST="${PGHOST:-localhost}" PGPORT="${PGPORT:-5432}" PGUSER="${PGUSER:-postgres}" PGPASSWORD="${PGPASSWORD:-postgres}"
# utc-everywhere: seeds and psql sessions run at UTC whatever the shell says.
export PGTZ=UTC
root="$(cd "$(dirname "$0")/.." && pwd)"

# Same locale as the postgres:18 image and the test templates (CodexDatabase.Locale).
psql -v ON_ERROR_STOP=1 -d postgres -c "DROP DATABASE IF EXISTS \"$db\" WITH (FORCE)" \
  -c "CREATE DATABASE \"$db\" TEMPLATE template0 ENCODING 'UTF8' LOCALE_PROVIDER libc LOCALE 'en_US.UTF-8'"
for folder in schema migrations seed; do
  for file in $(find "$root/db/$folder" -maxdepth 1 -name '*.sql' | LC_ALL=C sort); do
    echo "applying $folder/$(basename "$file")"
    psql -v ON_ERROR_STOP=1 -q -d "$db" -f "$file"
  done
done
echo "database $db is ready"
