import { memoryStore } from '@codex/core/platform';
import { describe, expect, it } from 'vitest';
import {
  createLastScreenMemory,
  formatLastScreen,
  lastScreenLink,
  lastScreenOfPath,
  parseLastScreen,
} from './lastScreen';

describe('last screen memory', () => {
  const memory = () => {
    const kv = memoryStore();
    return createLastScreenMemory(() => kv);
  };

  it('screenOf_WhenNothingWasRemembered_ShouldBeNull', () => {
    const m = memory();
    expect(m.lastBookId()).toBeNull();
    expect(m.screenOf('b1')).toBeNull();
  });

  it('remember_ShouldKeepTheBookAndItsScreen', () => {
    const m = memory();

    m.remember('b1', { tab: 'knowledge', pushed: 'e1' });

    expect(m.lastBookId()).toBe('b1');
    expect(m.screenOf('b1')).toEqual({ tab: 'knowledge', pushed: 'e1' });
  });

  it('remember_ForASecondBook_ShouldMakeItTheLastWithoutForgettingTheFirst', () => {
    const m = memory();
    m.remember('b1', { tab: 'write', pushed: null });

    m.remember('b2', { tab: 'plan', pushed: null });

    expect(m.lastBookId()).toBe('b2');
    expect(m.screenOf('b1')).toEqual({ tab: 'write', pushed: null });
    expect(m.screenOf('b2')).toEqual({ tab: 'plan', pushed: null });
  });

  it('remember_ShouldUseTheDesignKey', () => {
    const kv = memoryStore();
    createLastScreenMemory(() => kv).remember('b1', { tab: 'plan', pushed: null });
    expect(kv.getItem('codex.last.b1')).toBe('plan');
  });

  it('WhenTheStoreThrows_ShouldRememberNothingAndNotThrow', () => {
    const broken = createLastScreenMemory(() => {
      throw new Error('storage blocked');
    });

    expect(() => broken.remember('b1', { tab: 'plan', pushed: null })).not.toThrow();
    expect(broken.lastBookId()).toBeNull();
    expect(broken.screenOf('b1')).toBeNull();
  });

  it('screenOf_WhenTheStoredValueIsStale_ShouldBeNull', () => {
    const kv = memoryStore();
    kv.setItem('codex.last.b1', 'chat');
    expect(createLastScreenMemory(() => kv).screenOf('b1')).toBeNull();
  });
});

describe('parseLastScreen', () => {
  it.each([
    ['plan', { tab: 'plan', pushed: null }],
    ['knowledge/e1', { tab: 'knowledge', pushed: 'e1' }],
    ['settings/style-guide', { tab: 'settings', pushed: 'style-guide' }],
  ])('parses %s', (value, expected) => {
    expect(parseLastScreen(value)).toEqual(expected);
    expect(formatLastScreen(expected as never)).toBe(value);
  });

  it.each([null, '', 'chat', 'plan/', '/x'])('rejects %j', (value) => {
    expect(parseLastScreen(value)).toBeNull();
  });
});

describe('lastScreenOfPath', () => {
  it.each([
    ['/book/b1/plan', 'b1', { tab: 'plan', pushed: null }],
    ['/book/b1/write', 'b1', { tab: 'write', pushed: null }],
    ['/book/b1/knowledge/e1', 'b1', { tab: 'knowledge', pushed: 'e1' }],
    ['/book/b1/settings/style-guide', 'b1', { tab: 'settings', pushed: 'style-guide' }],
  ])('reads %s', (path, bookId, screen) => {
    expect(lastScreenOfPath(path)).toEqual({ bookId, screen });
  });

  it.each(['/', '/account', '/book/b1', '/book/b1/scene/s1', '/library/b1/plan'])(
    'ignores %s',
    (path) => {
      expect(lastScreenOfPath(path)).toBeNull();
    },
  );
});

describe('lastScreenLink', () => {
  it.each([
    [{ tab: 'plan', pushed: null }, '/book/[bookId]/plan'],
    [{ tab: 'write', pushed: null }, '/book/[bookId]/write'],
    [{ tab: 'knowledge', pushed: null }, '/book/[bookId]/knowledge'],
    [{ tab: 'knowledge', pushed: 'e1' }, '/book/[bookId]/knowledge/[entryId]'],
    [{ tab: 'settings', pushed: 'style-guide' }, '/book/[bookId]/settings/style-guide'],
    [{ tab: 'settings', pushed: 'unknown' }, '/book/[bookId]/settings'],
    [{ tab: 'plan', pushed: 'x' }, '/book/[bookId]/plan'],
  ] as const)('opens %j at %s', (screen, pathname) => {
    const link = lastScreenLink('b1', { ...screen });
    expect(link).toMatchObject({ pathname, params: { bookId: 'b1' } });
  });
});
