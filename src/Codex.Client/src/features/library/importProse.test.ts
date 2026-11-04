import { readdirSync, readFileSync } from 'node:fs';
import { join } from 'node:path';
import type { Node } from '@tiptap/pm/model';
import { describe, expect, it } from 'vitest';
import { parse, serialize } from '../../prose';

// The Markdown importer (C#, ImportProse) writes prose in the stored format. Every body it is pinned to produce must
// be canonical (serialize(parse(x)) === x) and must hold no directive: imported text never becomes a beat card.
const importFixtures = join(process.cwd(), '../Codex.TestSupport/Fixtures/import');
const expectedDirectory = join(importFixtures, 'markdown-small.expected');
const expectedFiles = readdirSync(expectedDirectory).filter(
  (f) => f.endsWith('.md') && f !== 'README.md',
);

type ProseCase = { name: string; input: string; stored: string };
const cases = JSON.parse(
  readFileSync(join(importFixtures, 'prose-cases.json'), 'utf8'),
) as ProseCase[];

// Exported by ImportProseGeneratedTests (C#): 3000 inputs from a fixed seed and what the importer stores for them.
type GeneratedCase = { Input: string; Stored: string };
const generated = JSON.parse(
  readFileSync(join(importFixtures, 'prose-generated.json'), 'utf8'),
) as GeneratedCase[];

// The pandoc import's stored chapters (PandocFolderParserTests pins them): canonical too, and holding only the
// directives the mapping emits on purpose.
const pandocDirectory = join(importFixtures, 'players-guide-campaign.expected');
const pandocFiles = readdirSync(pandocDirectory).filter((f) => f.endsWith('.md'));

function directives(doc: Node): string[] {
  const found: string[] = [];
  doc.descendants((node) => {
    if (node.type.name === 'directive') found.push(String(node.attrs.name));
  });
  return found;
}

describe('imported prose', () => {
  it('ExpectedFiles_ShouldExist', () => {
    expect(expectedFiles.length).toBeGreaterThan(0);
  });

  it.each(expectedFiles)('ImportedProse_EachFile_ShouldBeCanonical %s', (file) => {
    const markdown = readFileSync(join(expectedDirectory, file), 'utf8');
    expect(serialize(parse(markdown, 'prose'))).toBe(markdown);
  });

  it.each(cases)('ImportProseCase_Stored_ShouldBeCanonical $name', ({ stored }) => {
    expect(serialize(parse(stored, 'prose'))).toBe(stored);
  });

  it.each(cases)('ImportProseCase_Stored_ShouldHoldNoDirective $name', ({ stored }) => {
    expect(directives(parse(stored, 'prose'))).toEqual([]);
  });

  it('Generated_EveryStoredBody_ShouldBeCanonicalWithNoDirective', () => {
    expect(generated.length).toBe(3000);
    const failures = generated
      .map(({ Input, Stored }) => {
        const doc = parse(Stored, 'prose');
        const out = serialize(doc);
        const found = directives(doc);
        return out === Stored && found.length === 0
          ? null
          : { input: Input, stored: Stored, serialized: out, directives: found };
      })
      .filter((f) => f !== null);
    expect(failures.slice(0, 5)).toEqual([]);
    expect(failures.length).toBe(0);
  });
});

describe('imported pandoc prose', () => {
  it('PandocFiles_ShouldExist', () => {
    expect(pandocFiles.length).toBeGreaterThan(0);
  });

  it.each(pandocFiles)('PandocChapter_Stored_ShouldBeCanonical %s', (file) => {
    const markdown = readFileSync(join(pandocDirectory, file), 'utf8');
    expect(serialize(parse(markdown, 'prose'))).toBe(markdown);
  });

  it.each(pandocFiles)('PandocChapter_Stored_ShouldHoldOnlyMappedDirectives %s', (file) => {
    const doc = parse(readFileSync(join(pandocDirectory, file), 'utf8'), 'prose');
    for (const name of directives(doc)) expect(['figure', 'spell', 'pagebreak']).toContain(name);
  });
});
