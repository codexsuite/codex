# Seed ids

`ids.csv` names every fixed row of the seed (issue #16 Part 1) so seed SQL, tests and fixtures can refer to a row
before the seed exists. Columns: `kind,name,uuid,notes`.

- `kind` is a lowerCamel plural group (`books`, `kbEntries`, ...). It becomes the nested class in `SeedIds.cs`
  (`SeedIds.Books`) and the object key in `seedIds.ts` (`seedIds.books`).
- `name` is a PascalCase identifier, unique within its kind (`ProductEngineering`, `Ch5Sc2`). TypeScript lower-cases
  the first letter (`seedIds.books.productEngineering`).
- `uuid` is unique across the file and v7-shaped: `0199c0de-0000-7000-8000-<kind code><sequence>`, where the kind
  code is four hex digits (users 0001, series 0002, books 0003, acts 0004, chapters 0005, scenes 0006, beats 0007,
  kbProgressions 0008, aiGenerations 0009, kbCategories 000a, kbEntries 000b, labels 000c, references 000d,
  referenceUses 000e, quoteFlags 000f, permissionRequests 0010, assets 0011, figures 0012) and the sequence is
  eight hex digits counting from 1 in file order. New rows are appended to the end of their kind with the next
  sequence number; never renumber.
- `notes` is free text (quote it if it contains a comma).

Filler structure for the other books is not listed; the seed generates it deterministically.

After editing, run `node scripts/gen-seed-ids.mjs` to rewrite `src/Codex.Contracts/SeedIds.cs` and
`src/Codex.Client/src/test-support/seedIds.ts`. `node scripts/gen-seed-ids.mjs --check` fails when they are stale.
