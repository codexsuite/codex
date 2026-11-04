import { describe, expect, it } from 'vitest';
import { beatProseWords } from './beatProse';

const A = '0199c0de-0000-7000-8000-000700000001';
const B = '0199c0de-0000-7000-8000-000700000002';

describe('beatProseWords', () => {
  it('counts the prose under the beat up to the next beat', () => {
    const body = `::beat{id="${A}"}\n\nOne two three.\n\nFour five.\n\n::beat{id="${B}"}\n\nSix seven eight nine.\n`;
    expect(beatProseWords(body, A)).toBe(5);
    expect(beatProseWords(body, B)).toBe(4);
  });

  it('is 0 for a beat with no prose and for an unknown beat', () => {
    const body = `::beat{id="${A}"}\n\n::beat{id="${B}"}\n`;
    expect(beatProseWords(body, A)).toBe(0);
    expect(beatProseWords(body, B)).toBe(0);
    expect(beatProseWords(body, 'nope')).toBe(0);
  });

  it('counts past a directive under the beat, as Apply replaces everything up to the next beat', () => {
    const body = `::beat{id="${A}"}\n\nOne two.\n\n::kb-addition{id="0199c0de-0000-7000-8000-000700000009"}\n\nThree four five.\n\n::beat{id="${B}"}\n\nSix.\n`;
    expect(beatProseWords(body, A)).toBe(5);
  });
});
