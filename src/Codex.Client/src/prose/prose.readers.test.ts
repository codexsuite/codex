import { readFileSync } from 'node:fs';
import { join } from 'node:path';
import type { Node as PMNode } from '@tiptap/pm/model';
import { describe, expect, it } from 'vitest';
import readerCases from '../../../Codex.TestSupport/Fixtures/prose/reader-cases.json' with { type: 'json' };
import { parse } from './';

// The C# reader (Codex.Application.Prose.ProseDocument) must read the same cases the same way:
// src/Codex.UnitTest/Prose/ProseReaderCaseTests.cs.
const corpusDirectory = join(process.cwd(), '../../db/prose-corpus');

function blockKinds(nodes: readonly PMNode[]): string[] {
  return nodes.flatMap((node): string[] => {
    switch (node.type.name) {
      case 'paragraph':
        return node.content.size === 0 ? [] : ['paragraph'];
      case 'blockquote':
        return ['quote'];
      case 'bulletList':
      case 'orderedList':
        return ['list'];
      case 'container':
        return ['container', ...blockKinds(node.children), 'container'];
      case 'rawLatex':
        return ['fence'];
      case 'horizontalRule':
        return ['rule'];
      case 'opaqueBlock':
        return [(node.attrs.raw as string).startsWith('```') ? 'fence' : 'opaque'];
      default:
        return [node.type.name];
    }
  });
}

function embeds(doc: PMNode): string[] {
  const found: string[] = [];
  doc.descendants((node) => {
    if (node.type.name === 'directive' && node.attrs.ref) {
      found.push(`${node.attrs.name as string}:${node.attrs.ref as string}`);
    }
    if (!node.isTextblock) return true;
    let open = new Map<string, string>();
    node.forEach((child) => {
      const current = new Map<string, string>();
      for (const mark of child.marks) {
        const kind = mark.type.name === 'chapterLink' ? 'chapter' : mark.type.name;
        if (kind !== 'chapter' && kind !== 'ref') continue;
        current.set(kind, mark.attrs.id as string);
        if (open.get(kind) !== mark.attrs.id) found.push(`${kind}:${mark.attrs.id as string}`);
      }
      if (child.type.name === 'mention') found.push(`mention:${child.attrs.id as string}`);
      if (child.type.name === 'refMarker') found.push(`ref:${child.attrs.id as string}`);
      open = current;
    });
    return false;
  });
  return found.map((e) => e.toLowerCase());
}

describe('prose readers agree', () => {
  it.each(readerCases.cases)('ReaderCase_ShouldMatchSharedExpectation $name', (c) => {
    const doc = parse(c.markdown, 'prose');

    expect({ blocks: blockKinds(doc.children), embeds: embeds(doc) }).toEqual({
      blocks: c.blocks,
      embeds: c.embeds,
    });
  });

  it.each(readerCases.corpus)('Corpus_Embeds_ShouldMatchSharedExpectation $file', (c) => {
    const doc = parse(readFileSync(join(corpusDirectory, c.file), 'utf8'), 'prose');

    expect(embeds(doc)).toEqual(c.embeds);
  });
});
