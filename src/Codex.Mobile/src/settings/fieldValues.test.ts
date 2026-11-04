import { describe, expect, it } from 'vitest';
import {
  countText,
  formatsText,
  parseWholeNumber,
  preview,
  publisherText,
  quoteLimitText,
  statusLabel,
} from './fieldValues';

describe('fieldValues', () => {
  it('statusLabel_WhenKnown_ShouldCapitalise', () => {
    expect(statusLabel('revising')).toBe('Revising');
    expect(statusLabel('odd')).toBe('odd');
  });

  it('publisherText_ShouldTellSelfPublishedFromNotSet', () => {
    expect(publisherText(null)).toBe('Not set');
    expect(publisherText('')).toBe('Self-published');
    expect(publisherText('Ashfall Press')).toBe('Ashfall Press');
  });

  it('formatsText_ShouldListInTheDesktopOrder', () => {
    expect(formatsText(['audio', 'print'])).toBe('Print, Audiobook');
    expect(formatsText([])).toBe('Not set');
  });

  it('countText_And_quoteLimitText_ShouldGroupThousands', () => {
    expect(countText(null)).toBe('Not set');
    expect(countText(120000)).toBe('120,000');
    expect(quoteLimitText(300)).toBe('300 words prose');
  });

  it('preview_WhenLong_ShouldCutOnAWord', () => {
    expect(preview(null)).toBeUndefined();
    expect(preview('  one\n two  ')).toBe('one two');
    const long = 'word '.repeat(40);
    const out = preview(long, 22);
    expect(out).toBe('word word word word…');
  });

  it('parseWholeNumber_ShouldValidate', () => {
    expect(parseWholeNumber('', { allowBlank: true })).toEqual({ ok: true, value: 0 });
    expect(parseWholeNumber('', { allowBlank: false }).ok).toBe(false);
    expect(parseWholeNumber('90,000', { allowBlank: true })).toEqual({ ok: true, value: 90000 });
    expect(parseWholeNumber('12a', { allowBlank: true })).toEqual({
      ok: false,
      message: 'Use digits only.',
    });
    expect(parseWholeNumber('0', { allowBlank: false, min: 1 }).ok).toBe(false);
  });
});
