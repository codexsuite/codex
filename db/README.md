Apply order: `schema/` -> `migrations/` -> `seed/`, each folder in file-name order, via DbUp (`Codex.Application.Database`).
The app applies schema + migrations at startup (`CODEX_DB_MIGRATE`, default true); test fixtures also apply the seed.
