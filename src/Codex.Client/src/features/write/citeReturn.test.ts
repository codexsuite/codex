import { describe, expect, it } from 'vitest';
import { parseDeepLink } from '@codex/core/shell/deepLink';
import { referencesLink, writeLink } from '@codex/core/shell/links';
import {
  parseSelection,
  passageOf,
  returnLink,
  returnTarget,
  selectionIn,
  selectionText,
  writeReturnLink,
} from './citeReturn';

const parse = (url: string) => {
  const u = new URL(url, 'http://codex.test');
  return parseDeepLink(u.pathname, u.search, u.hash);
};

describe('the New source… detour', () => {
  const body = 'He looked back. Then he looked back.\n';
  const sel = selectionText(selectionIn(body, 'looked back.', 2, 7)!);

  it('keeps the selection as a range of the saved prose and reads the same words back', () => {
    expect(sel).toBe('24-36@7');
    expect(passageOf(body, parseSelection(sel)!)).toEqual({
      passage: 'looked back.',
      occurrence: 2,
    });
    expect(parseSelection('9-3@1')).toBeNull();
    expect(parseSelection('words')).toBeNull();
  });

  it('goes to Add source and comes back to Write with the source to cite', () => {
    const out = referencesLink('b1', {
      isNew: true,
      returnTo: writeLink('b1', { scene: '7.1' }),
      sel,
    });
    const there = parse(out);
    expect(there.ok && there.link).toMatchObject({ screen: 'references', isNew: true, sel });
    const returnTo = there.ok ? there.link.returnTo! : '';
    expect(returnTarget(returnTo)).toEqual({ bookId: 'b1', scene: '7.1' });

    const back = parse(returnLink('b1', returnTo, { sel, cite: 's9', title: 'Lean & Mean' })!);
    expect(back.ok && back.link).toMatchObject({
      screen: 'write',
      scene: '7.1',
      sel,
      cite: 's9',
      title: 'Lean & Mean',
    });
    expect(returnLink('b1', returnTo)).toBe('/books/b1/write#scene=7.1');
  });

  it('asks Write for a paraphrase of a source at a scene', () => {
    const link = parse(writeReturnLink('b1', { scene: '8.1', ref: 'v1', paraphrase: true }));
    expect(link.ok && link.link).toMatchObject({ scene: '8.1', ref: 'v1', paraphrase: true });
  });

  it('follows no return path but Write in the same book', () => {
    expect(returnLink('b1', '/books/b1/plan#scene=2.1', { cite: 's9' })).toBeNull();
    expect(returnLink('b1', '/books/b2/write#scene=2.1')).toBeNull();
    expect(returnLink('b1', '/\\evil.example')).toBeNull();
    expect(returnLink('b1', 'https://evil.example/books/b1/write')).toBeNull();
  });
});
