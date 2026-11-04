import { describe, expect, it } from 'vitest';
import { serverLine } from './serverLine';

describe('serverLine', () => {
  it('names the origin and the version', () => {
    expect(serverLine('https://codex.example.com', '1.0.0+abc')).toBe(
      'https://codex.example.com · Codex 1.0.0',
    );
  });

  it.each([null, '', '  '])('shows the origin alone for version %j', (version) => {
    expect(serverLine('https://codex.example.com', version)).toBe('https://codex.example.com');
  });
});
