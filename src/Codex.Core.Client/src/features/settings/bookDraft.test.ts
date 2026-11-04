import { describe, expect, it } from 'vitest';
import { changesOf, wordCount, type BookDraft } from './bookDraft';

const saved: BookDraft = {
  title: 'Hollow Throne',
  authorName: 'Joel Dickson',
  status: 'revising',
  targetWords: '120000',
  styleGuide: 'Close third.',
};

describe('changesOf', () => {
  it('Changes_WhenNothingEdited_ShouldBeEmpty', () => {
    expect(changesOf(saved, saved)).toEqual({});
  });

  it('Changes_WhenOneFieldEdited_ShouldSendOnlyThatField', () => {
    expect(changesOf({ ...saved, status: 'writing' }, saved)).toEqual({ status: 'writing' });
  });

  it('Changes_WhenTitleBlank_ShouldNotSendIt', () => {
    expect(changesOf({ ...saved, title: '   ' }, saved)).toEqual({});
  });

  it('Changes_WhenTargetCleared_ShouldSendZero', () => {
    expect(changesOf({ ...saved, targetWords: '' }, saved)).toEqual({ targetWords: 0 });
  });

  it('Changes_WhenAuthorCleared_ShouldSendEmptyText', () => {
    expect(changesOf({ ...saved, authorName: '' }, saved)).toEqual({ authorName: '' });
  });
});

describe('wordCount', () => {
  it('WordCount_WhenBlank_ShouldBeZero', () => {
    expect(wordCount('  \n ')).toBe(0);
  });
  it('WordCount_WhenSentence_ShouldCountWords', () => {
    expect(wordCount('Close third person,\npast tense.')).toBe(5);
  });
});
