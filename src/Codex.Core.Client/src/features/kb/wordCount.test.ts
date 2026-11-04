import { describe, expect, it } from 'vitest';
import { wordCount } from './wordCount';

describe('wordCount', () => {
  it('WordCount_WhenEmpty_ShouldBeZero', () => {
    expect(wordCount('')).toBe(0);
    expect(wordCount('  \n\n ')).toBe(0);
  });

  it('WordCount_WhenPlainText_ShouldCountWords', () => {
    expect(wordCount('A measure that becomes a target.')).toBe(6);
  });

  it('WordCount_WhenMarkdownMarks_ShouldNotCountThem', () => {
    expect(wordCount('# Origin\n\n> Quoted words\n\n- one\n- two')).toBe(5);
  });

  it('WordCount_WhenMention_ShouldCountTheNameNotTheTarget', () => {
    expect(wordCount("See [Conway's Law](kb:0199c0de-0000) and **more**.")).toBe(5);
  });
});
