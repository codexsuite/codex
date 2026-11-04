import { readdirSync, readFileSync } from 'node:fs';
import { join } from 'node:path';
import { describe, expect, it } from 'vitest';
import { parse, serialize } from './';

// The Players Guide after the LaTeX import mapping (src/Codex.TestSupport/Fixtures/players-guide/README.md).
const mappedDirectory = join(process.cwd(), '../Codex.TestSupport/Fixtures/players-guide/mapped');
const chapters = readdirSync(mappedDirectory).filter((f) => f.endsWith('.md'));

describe('Players Guide fixture', () => {
  it('Mapped_HasAllChapters', () => {
    expect(chapters).toHaveLength(10);
  });

  it.each(chapters)('Mapped_EachChapter_ShouldRoundTripExactly %s', (file) => {
    const markdown = readFileSync(join(mappedDirectory, file), 'utf8');
    const doc = parse(markdown, 'prose');
    expect(() => doc.check()).not.toThrow();
    expect(serialize(doc)).toBe(markdown);
  });

  it('Mapped_AllChapters_ShouldReadEveryPipeLineAsATableRow', () => {
    let pipeLines = 0;
    let rows = 0;
    for (const file of chapters) {
      const markdown = readFileSync(join(mappedDirectory, file), 'utf8');
      pipeLines += markdown.split('\n').filter((line) => line.startsWith('|')).length;
      parse(markdown, 'prose').descendants((node) => {
        if (node.type.name === 'table') {
          // A table's delimiter row is not a node, so count it with the rows.
          rows += node.childCount + 1;
        }
        return true;
      });
    }
    expect(rows).toBe(pipeLines);
  });
});
