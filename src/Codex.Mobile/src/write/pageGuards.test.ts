import { configurePlatform, memoryStore } from '@codex/core/platform';
import { beforeAll, describe, expect, it } from 'vitest';
import { decideNavigation, editorPageUrl, isEditorPage } from './pageGuards';

beforeAll(() => {
  configurePlatform({
    persistentStore: memoryStore,
    sessionStore: memoryStore,
    isOffline: () => false,
    onOnline: () => () => {},
    onUnauthorized: () => {},
    apiOrigin: () => 'https://codex.test',
    authHeaders: () => ({}),
  });
});

describe('pageGuards', () => {
  it('EditorPageUrl_ShouldBeTheServersEditorPage', () => {
    expect(editorPageUrl()).toBe('https://codex.test/mobile-editor.html');
  });

  it.each([
    ['https://codex.test/mobile-editor.html', true],
    ['https://codex.test/mobile-editor.html#x', true],
    ['https://codex.test/', false],
    ['https://evil.test/mobile-editor.html', false],
    ['https://codex.test.evil.test/mobile-editor.html', false],
    ['about:blank', false],
    [undefined, false],
  ])('IsEditorPage_%s_ShouldBe_%s', (url, expected) => {
    expect(isEditorPage(url)).toBe(expected);
  });

  it.each([
    ['https://codex.test/mobile-editor.html', 'allow'],
    ['about:blank', 'allow'],
    ['https://example.com/a', 'external'],
    ['http://example.com/a', 'external'],
    ['intent://x#Intent;end', 'block'],
    ['javascript:alert(1)', 'block'],
    ['https://codex.test/other', 'external'],
  ])('DecideNavigation_%s_ShouldBe_%s', (url, expected) => {
    expect(decideNavigation(url)).toBe(expected);
  });
});
