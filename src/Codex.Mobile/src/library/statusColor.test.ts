import { describe, expect, it } from 'vitest';
import { lightTheme } from '~/theme/themes';
import { statusColor } from './statusColor';

describe('statusColor', () => {
  it('uses the theme colour of a known status', () => {
    expect(statusColor(lightTheme, 'writing')).toBe(lightTheme.status.writing);
  });

  it('falls back to muted for a status the theme does not know', () => {
    expect(statusColor(lightTheme, 'needs_check')).toBe(lightTheme.colors.muted);
  });
});
