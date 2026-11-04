import { readFileSync } from 'node:fs';
import { describe, expect, it } from 'vitest';
import { seedIds } from './seedIds';

// Vitest runs from the client folder.
const csv = readFileSync('../../db/seed/ids.csv', 'utf8');

function readCsv() {
  return csv
    .split('\n')
    .slice(1)
    .filter((line) => line.length > 0)
    .map((line) => {
      const [kind, name, uuid] = line.split(',', 3);
      return { kind, key: name[0].toLowerCase() + name.slice(1), uuid };
    });
}

describe('seedIds', () => {
  it('matches the CSV in both directions', () => {
    const fromCsv = Object.fromEntries(readCsv().map((r) => [`${r.kind}.${r.key}`, r.uuid]));
    const fromCode = Object.fromEntries(
      Object.entries(seedIds).flatMap(([kind, items]) =>
        Object.entries(items).map(([key, uuid]) => [`${kind}.${key}`, uuid]),
      ),
    );

    expect(fromCode).toEqual(fromCsv);
  });

  it('has unique uuids and names', () => {
    const rows = readCsv();

    expect(new Set(rows.map((r) => r.uuid)).size).toBe(rows.length);
    expect(new Set(rows.map((r) => `${r.kind}.${r.key}`)).size).toBe(rows.length);
  });
});
