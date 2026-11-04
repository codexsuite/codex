import { describe, expect, it } from 'vitest';
import { splitRefs } from './draftText';

const use = '0199c0de-0000-7000-8000-000e00000004';

describe('splitRefs', () => {
  it('keeps plain text whole', () => {
    expect(splitRefs('Nothing cited.')).toEqual([{ text: 'Nothing cited.', useId: null }]);
  });

  it('splits a ref link from the words around it', () => {
    expect(splitRefs(`Metrics [rot when targeted](ref:${use}).`)).toEqual([
      { text: 'Metrics ', useId: null },
      { text: 'rot when targeted', useId: use },
      { text: '.', useId: null },
    ]);
  });

  it('gives a bare marker no words', () => {
    expect(splitRefs(`A pause.[^](ref:${use})`)).toEqual([
      { text: 'A pause.', useId: null },
      { text: '', useId: use },
    ]);
  });
});
