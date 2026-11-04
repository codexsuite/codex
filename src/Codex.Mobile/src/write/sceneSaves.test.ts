import type { Scheduler } from '@codex/core/editing/autosaveController';
import { ApiRequestError } from '@codex/core/api/client';
import { createSaveStatusStore } from '@codex/core/editing/saveStatusStore';
import { liveAutosaves } from '~/book/liveAutosaves';
import { createBookSaveStatus } from '~/book/saveStatus';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { SceneSaves, type SceneSavesDeps, type UnsavedDraft } from './sceneSaves';

// A scheduler a test fires by hand, so no test waits for the 3 s autosave delay.
function manualScheduler() {
  const pending = new Map<number, () => void>();
  let next = 1;
  const scheduler: Scheduler = {
    setTimeout(callback) {
      pending.set(next, callback);
      return next++;
    },
    clearTimeout(handle) {
      pending.delete(handle as number);
    },
  };
  return { scheduler, fire: () => [...pending.values()].forEach((run) => run()) };
}

beforeEach(() => liveAutosaves.reset());

function setup(overrides: Partial<SceneSavesDeps> = {}) {
  const bar = createSaveStatusStore();
  const timers = manualScheduler();
  const deps = {
    bookId: 'book-1',
    sources: createBookSaveStatus(bar).forBook('book-1'),
    saveProse: vi.fn(async (_id: string, _body: string, version: number) => ({
      version: version + 1,
    })),
    onSaved: vi.fn(),
    onConflict: vi.fn(),
    onForbidden: vi.fn(),
    keepDraft: vi.fn(),
    readDraft: vi.fn((): UnsavedDraft | undefined => undefined),
    scheduler: timers.scheduler,
    isOffline: () => false,
    ...overrides,
  };
  return { saves: new SceneSaves(deps), deps, bar, timers };
}

describe('SceneSaves', () => {
  it('Change_ShouldNotSaveUntilTheDelayThenSaveWithTheLoadedVersion', async () => {
    const { saves, deps, bar, timers } = setup();
    saves.open('s1', { body: 'Old.\n', version: 3 });

    saves.change('s1', 'New.\n');
    expect(bar.getState()).toBe('unsaved');
    expect(deps.saveProse).not.toHaveBeenCalled();

    timers.fire();
    await saves.settle();

    expect(deps.saveProse).toHaveBeenCalledWith('s1', 'New.\n', 3);
    expect(deps.onSaved).toHaveBeenCalledWith('s1', 'New.\n', 4);
    expect(saves.saved('s1')).toEqual({ body: 'New.\n', version: 4 });
    expect(bar.getState()).toBe('saved');
  });

  it('Change_WhenTheTextIsTheLoadedText_ShouldNotSave', async () => {
    const { saves, deps, bar } = setup();
    saves.open('s1', { body: 'Same.\n', version: 1 });

    saves.change('s1', 'Same.\n');
    await saves.settle();

    expect(deps.saveProse).not.toHaveBeenCalled();
    expect(bar.getState()).toBe('saved');
  });

  it('Settle_ShouldSaveEveryPendingSceneWithoutWaitingForTheTimer', async () => {
    const { saves, deps } = setup();
    saves.open('s1', { body: 'a', version: 1 });
    saves.open('s2', { body: 'b', version: 7 });
    saves.change('s1', 'a2');
    saves.change('s2', 'b2');

    const clean = await saves.settle();

    expect(clean).toBe(true);
    expect(deps.saveProse).toHaveBeenCalledWith('s1', 'a2', 1);
    expect(deps.saveProse).toHaveBeenCalledWith('s2', 'b2', 7);
  });

  it('Settle_WhenTextWasTypedWhileASaveWasInFlight_ShouldSaveItToo', async () => {
    let release: () => void = () => {};
    const { saves, deps } = setup({
      saveProse: vi.fn(
        (_id: string, _body: string, version: number) =>
          new Promise<{ version: number }>((resolve) => {
            release = () => resolve({ version: version + 1 });
          }),
      ),
    });
    saves.open('s1', { body: 'a', version: 1 });
    saves.change('s1', 'ab');
    const first = saves.settle();
    await vi.waitFor(() => expect(deps.saveProse).toHaveBeenCalledTimes(1));

    saves.change('s1', 'abc');
    release();
    await vi.waitFor(() => expect(deps.saveProse).toHaveBeenCalledTimes(2));
    release();
    await first;

    expect(deps.saveProse).toHaveBeenLastCalledWith('s1', 'abc', 2);
    expect(saves.saved('s1')).toEqual({ body: 'abc', version: 3 });
  });

  it('Settle_IfTheControllerReportsAConflict_ShouldTellTheCallerAndNeverRetry', async () => {
    const conflict = new ApiRequestError('Stale', 409, 'conflict');
    const { saves, deps, bar } = setup({ saveProse: vi.fn().mockRejectedValue(conflict) });
    saves.open('s1', { body: 'a', version: 1 });
    saves.change('s1', 'b');

    const clean = await saves.settle();
    await saves.settle();

    expect(clean).toBe(false);
    expect(bar.getState()).toBe('conflict');
    expect(deps.onConflict).toHaveBeenCalledWith('s1');
    expect(deps.saveProse).toHaveBeenCalledTimes(1);
  });

  it('Settle_WhenTheServerFails_ShouldKeepTheEditAndSayRetry', async () => {
    const down = new ApiRequestError('Down', 500, 'internal_error');
    const { saves, bar } = setup({ saveProse: vi.fn().mockRejectedValue(down) });
    saves.open('s1', { body: 'a', version: 1 });
    saves.change('s1', 'b');

    expect(await saves.settle()).toBe(false);
    expect(bar.getState()).toBe('retry');
  });

  it('Settle_WhenForbidden_ShouldTellTheCaller', async () => {
    const forbidden = new ApiRequestError('No', 403, 'forbidden');
    const { saves, deps } = setup({ saveProse: vi.fn().mockRejectedValue(forbidden) });
    saves.open('s1', { body: 'a', version: 1 });
    saves.change('s1', 'b');

    await saves.settle();

    expect(deps.onForbidden).toHaveBeenCalledWith('s1');
  });

  it('Reset_ShouldAdoptTheServersCopyAndSaveFromItsVersion', async () => {
    const { saves, deps, bar } = setup();
    saves.open('s1', { body: 'a', version: 1 });
    saves.markConflict('s1');
    expect(bar.getState()).toBe('conflict');

    saves.reset('s1', 'server', 9);
    expect(bar.getState()).toBe('saved');
    saves.change('s1', 'server plus');
    await saves.settle();

    expect(deps.saveProse).toHaveBeenCalledWith('s1', 'server plus', 9);
  });

  it('Retain_ShouldStopSavingCleanScenesOutsideTheChapter', async () => {
    const { saves } = setup();
    saves.open('s1', { body: 'a', version: 1 });
    saves.open('s2', { body: 'b', version: 1 });

    saves.retain(['s1']);

    expect(saves.has('s1')).toBe(true);
    expect(saves.has('s2')).toBe(false);
  });

  it('Retain_ShouldKeepASceneWithUnsavedTextAliveAndGiveTheTextBackOnOpen', async () => {
    const { saves, deps, bar } = setup({
      saveProse: vi.fn().mockRejectedValue(new ApiRequestError('Down', 500, 'internal_error')),
    });
    saves.open('s1', { body: 'a', version: 1 });
    saves.change('s1', 'typed');
    expect(await saves.settle()).toBe(false);

    saves.retain([]);

    expect(saves.has('s1')).toBe(true);
    expect(bar.getState()).toBe('retry');
    expect(deps.keepDraft).toHaveBeenCalledWith('s1', { body: 'typed', version: 1 });
    expect(saves.open('s1', { body: 'a', version: 1 })).toEqual({ body: 'typed', version: 1 });
  });

  it('Open_WhenAKeptDraftFitsTheServersVersion_ShouldGiveTheDraftAndSaveItNext', async () => {
    const { saves, deps } = setup({
      readDraft: vi.fn((): UnsavedDraft | undefined => ({ body: 'draft', version: 5 })),
    });

    const shown = saves.open('s1', { body: 'server', version: 5 });
    await saves.settle();

    expect(shown).toEqual({ body: 'draft', version: 5 });
    expect(deps.saveProse).toHaveBeenCalledWith('s1', 'draft', 5);
    expect(deps.keepDraft).toHaveBeenLastCalledWith('s1', null);
  });

  it('Open_WhenTheKeptDraftIsTheServersText_ShouldShowTheServersCopy', async () => {
    const { saves, deps } = setup({
      readDraft: vi.fn((): UnsavedDraft | undefined => ({ body: 'same', version: 5 })),
    });

    expect(saves.open('s1', { body: 'same', version: 5 })).toEqual({ body: 'same', version: 5 });
    await saves.settle();

    expect(deps.saveProse).not.toHaveBeenCalled();
  });

  it('Dispose_ShouldSendTheUnsavedTextKeepADraftAndRetireOnceItIsSaved', async () => {
    const { saves, deps, bar } = setup();
    saves.open('s1', { body: 'a', version: 1 });
    saves.open('s2', { body: 'b', version: 1 });
    saves.change('s1', 'typed');

    saves.dispose();

    expect(deps.keepDraft).toHaveBeenCalledWith('s1', { body: 'typed', version: 1 });
    expect(saves.has('s1')).toBe(false);
    await vi.waitFor(() => expect(deps.saveProse).toHaveBeenCalledWith('s1', 'typed', 1));
    await vi.waitFor(() => expect(liveAutosaves.get('book-1', 'write:s1')).toBeUndefined());
    expect(liveAutosaves.get('book-1', 'write:s2')).toBeUndefined();
    expect(bar.getState()).toBe('saved');
  });

  it('Open_ByANewScreenWhileTheOldControllerIsStillTrying_ShouldAdoptItAndItsPendingText', async () => {
    const offline = vi.fn(() => true);
    const { saves, deps, bar } = setup({
      isOffline: offline,
      saveProse: vi.fn().mockRejectedValue(new TypeError('Network request failed')),
    });
    saves.open('s1', { body: 'a', version: 1 });
    saves.change('s1', 'typed offline');
    saves.dispose();
    await vi.waitFor(() => expect(bar.getState()).toBe('offline'));

    // Another screen of the same book (a new SceneSaves) opens the scene again.
    const again = new SceneSaves(deps);
    const shown = again.open('s1', { body: 'a', version: 1 });

    expect(shown).toEqual({ body: 'typed offline', version: 1 });
    expect(again.unsaved('s1')).toBe(true);
    expect(bar.getState()).toBe('offline');
    expect(deps.saveProse).toHaveBeenCalledTimes(1);
  });

  it('Dispose_OfAScreenThatWasAdoptedFromWhileItSettled_ShouldLeaveTheNewScreensControllerAndSourceAlone', async () => {
    let land: (result: { version: number }) => void = () => undefined;
    const held = vi.fn(
      () =>
        new Promise<{ version: number }>((resolve) => {
          land = resolve;
        }),
    );
    const { saves: old, deps, bar } = setup({ saveProse: held });
    old.open('s1', { body: 'a', version: 1 });
    old.change('s1', 'typed');
    // The old screen unmounts: settle (its PUT is out), then dispose.
    const leaving = old.settle().finally(() => old.dispose());
    await vi.waitFor(() => expect(held).toHaveBeenCalledTimes(1));

    // The same book's Write opens again before that has finished and takes the scene over.
    const again = new SceneSaves(deps);
    expect(again.open('s1', { body: 'a', version: 1 })).toEqual({ body: 'typed', version: 1 });
    land({ version: 2 });
    await leaving;

    again.change('s1', 'typed more');
    expect(bar.getState()).toBe('unsaved');
    expect(liveAutosaves.get('book-1', 'write:s1')).toBeDefined();
    expect(again.unsaved('s1')).toBe(true);
  });

  it('Dispose_WhenTheSaveKeepsFailing_ShouldRetryWithBackoffThenRetireAndClearTheDot', async () => {
    vi.useFakeTimers();
    try {
      const down = new ApiRequestError('Down', 500, 'internal_error');
      const { saves, deps, bar } = setup({ saveProse: vi.fn().mockRejectedValue(down) });
      saves.open('s1', { body: 'a', version: 1 });
      saves.change('s1', 'typed');

      saves.dispose();
      await vi.advanceTimersByTimeAsync(0);
      expect(bar.getState()).toBe('retry');
      expect(deps.saveProse).toHaveBeenCalledTimes(1);

      await vi.advanceTimersByTimeAsync(5_000);
      expect(deps.saveProse).toHaveBeenCalledTimes(2);
      await vi.advanceTimersByTimeAsync(15_000);
      expect(deps.saveProse).toHaveBeenCalledTimes(3);
      expect(bar.getState()).toBe('retry');
      await vi.advanceTimersByTimeAsync(45_000);
      expect(deps.saveProse).toHaveBeenCalledTimes(4);

      expect(bar.getState()).toBe('saved');
      expect(liveAutosaves.get('book-1', 'write:s1')).toBeUndefined();
      expect(deps.keepDraft).toHaveBeenLastCalledWith('s1', { body: 'typed', version: 1 });
      await vi.advanceTimersByTimeAsync(120_000);
      expect(deps.saveProse).toHaveBeenCalledTimes(4);
    } finally {
      vi.useRealTimers();
    }
  });

  it('Save_WhenTheServerBumpsTheVersion_ShouldSendTheNewVersionNext', async () => {
    const { saves, deps } = setup({
      saveProse: vi.fn(async (_id: string, _body: string, version: number) => ({
        version: version === 1 ? 7 : version + 1,
      })),
    });
    saves.open('s1', { body: 'a', version: 1 });
    saves.change('s1', 'b');
    await saves.settle();
    saves.change('s1', 'c');
    await saves.settle();

    expect(deps.saveProse).toHaveBeenNthCalledWith(1, 's1', 'b', 1);
    expect(deps.saveProse).toHaveBeenNthCalledWith(2, 's1', 'c', 7);
  });
});
