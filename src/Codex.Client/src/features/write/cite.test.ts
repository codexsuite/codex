import { describe, expect, it } from 'vitest';
import type { Node as PmNode } from '@tiptap/pm/model';
import { EditorState, TextSelection } from '@tiptap/pm/state';
import { parse } from '../../prose';
import { citeSelectionKey, citeSelectionPlugin, citeTarget, lastSentence } from './citeSelection';
import { passageAt } from './citePassage';

// The document position where the n-th (1-based) `text` starts, inside a single text node.
function at(doc: PmNode, text: string, nth = 1): number {
  let seen = 0;
  let found = -1;
  doc.descendants((node, pos) => {
    if (found >= 0 || !node.isText) return found < 0;
    for (let i = node.text!.indexOf(text); i >= 0; i = node.text!.indexOf(text, i + 1)) {
      seen += 1;
      if (seen === nth) {
        found = pos + i;
        return false;
      }
    }
    return true;
  });
  if (found < 0) throw new Error(`"${text}" is not in the doc`);
  return found;
}

const stateAt = (body: string, caret: number | ((doc: PmNode) => number)) => {
  const doc = parse(body, 'prose');
  const state = EditorState.create({ doc, plugins: [citeSelectionPlugin()] });
  const pos = typeof caret === 'number' ? caret : caret(doc);
  return state.apply(state.tr.setSelection(TextSelection.create(doc, pos)));
};

describe('passageAt', () => {
  it('gives the Markdown of the words and which match of it they are', () => {
    const doc = parse('One fish. Two fish. One fish.\n', 'prose');
    const from = at(doc, 'One fish.', 2);
    expect(passageAt(doc, from, from + 'One fish.'.length)).toEqual({
      passage: 'One fish.',
      occurrence: 2,
    });
  });

  it('keeps inline formatting inside the passage and counts matches in earlier paragraphs', () => {
    const doc = parse('A *bold* claim.\n\nThen a *bold* claim.\n', 'prose');
    const from = at(doc, 'a ');
    const to = at(doc, ' claim.', 2) + ' claim.'.length;
    expect(passageAt(doc, from, to)).toEqual({ passage: 'a *bold* claim.', occurrence: 1 });
    const first = at(doc, 'bold', 1);
    expect(passageAt(doc, first, first + 4)).toEqual({ passage: '*bold*', occurrence: 1 });
    const second = at(doc, 'bold', 2);
    expect(passageAt(doc, second, second + 4)).toEqual({ passage: '*bold*', occurrence: 2 });
  });

  it('is null for an empty range', () => {
    const doc = parse('Words.\n', 'prose');
    expect(passageAt(doc, 1, 1)).toBeNull();
  });

  it('leaves out whitespace at either end, as a double-click selects the space after a word', () => {
    const doc = parse('Over there now.\n', 'prose');
    const there = at(doc, 'there');
    expect(passageAt(doc, there, there + 'there '.length)).toEqual({
      passage: 'there',
      occurrence: 1,
    });
    expect(passageAt(doc, there - 1, there + 'there'.length)).toEqual({
      passage: 'there',
      occurrence: 1,
    });
    expect(passageAt(doc, there - 1, there)).toBeNull();
  });

  it('is null for words in inline code', () => {
    const doc = parse('Run `foo bar` first.\n', 'prose');
    const code = at(doc, 'foo bar');
    expect(passageAt(doc, code, code + 'foo bar'.length)).toBeNull();
    expect(passageAt(doc, code - 'Run '.length, code + 3)).toBeNull();
  });
});

describe('lastSentence', () => {
  it('is the run from the last sentence end to the caret, leaving out a /query and trailing spaces', () => {
    const text = 'First one. Second one! ';
    expect(lastSentence(text)).toEqual({ start: 11, end: 22 });
    expect(lastSentence('First one. Second one. /sou')).toEqual({ start: 11, end: 22 });
    expect(lastSentence('First one. Half a')).toEqual({ start: 11, end: 17 });
    expect(lastSentence('He said "Go." Then left.')).toEqual({ start: 14, end: 24 });
  });

  it('is null when there are no words before the caret', () => {
    expect(lastSentence('')).toBeNull();
    expect(lastSentence(' /')).toBeNull();
    expect(lastSentence('First one. ')).toEqual({ start: 0, end: 10 });
  });
});

describe('citeTarget', () => {
  it('is the sentence before the caret when no selection was kept', () => {
    const state = stateAt('Alpha beta. Gamma delta.\n', (doc) => at(doc, 'Gamma delta.') + 12);
    const target = citeTarget(state)!;
    expect(state.doc.textBetween(target.from, target.to)).toBe('Gamma delta.');
    expect(target.space).toBeUndefined();
  });

  it('is null at the start of a paragraph', () => {
    expect(citeTarget(stateAt('Alpha beta.\n', 1))).toBeNull();
  });

  it('is the kept selection while the caret is after its ` /query`, and the space to remove', () => {
    const doc = parse('Alpha beta. Gamma delta.\n', 'prose');
    const from = at(doc, 'Alpha');
    const to = from + 'Alpha beta.'.length;
    let state = EditorState.create({ doc, plugins: [citeSelectionPlugin()] });
    const tr = state.tr.insertText(' /sou', to, to);
    tr.setSelection(TextSelection.create(tr.doc, to + 5));
    tr.setMeta(citeSelectionKey, { from, to, space: to });
    state = state.apply(tr);

    expect(citeTarget(state)).toEqual({ from, to, space: to });

    // Once the caret moves off the `/query` the menu is gone: its text goes and the kept selection no longer counts.
    state = state.applyTransaction(state.tr.setSelection(TextSelection.create(state.doc, 1))).state;
    expect(state.doc.textContent).toBe('Alpha beta. Gamma delta.');
    expect(citeTarget(state)).toBeNull();
  });

  it('keeps the ` /query` while the caret is in it', () => {
    const doc = parse('Alpha beta.\n', 'prose');
    const to = at(doc, 'Alpha') + 'Alpha beta.'.length;
    let state = EditorState.create({ doc, plugins: [citeSelectionPlugin()] });
    const tr = state.tr.insertText(' /so', to, to);
    tr.setSelection(TextSelection.create(tr.doc, to + 3));
    state = state.applyTransaction(tr.setMeta(citeSelectionKey, { from: 1, to, space: to })).state;
    expect(state.doc.textContent).toBe('Alpha beta. /so');
  });
});
