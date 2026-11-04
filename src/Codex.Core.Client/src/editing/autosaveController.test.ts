import { describe, expect, it, vi } from 'vitest';
import { ApiRequestError } from '../api/client';
import { configurePlatform, platform } from '../platform';
import { onForbidden } from '../realtime/forbidden';
import { AutosaveController, type Scheduler } from './autosaveController';
import { createSaveStatusStore } from './saveStatusStore';

// A scheduler the test fires by hand.
function manualScheduler() {
  const pending = new Map<number, { callback: () => void; ms: number }>();
  let next = 1;
  const scheduler: Scheduler = {
    setTimeout: (callback, ms) => {
      pending.set(next, { callback, ms });
      return next++;
    },
    clearTimeout: (handle) => void pending.delete(handle as number),
  };
  return {
    scheduler,
    pending,
    fire: () => [...pending.values()].forEach((p) => p.callback()),
  };
}

function setup(save: (value: string, version: number) => Promise<{ version: number }>) {
  const clock = manualScheduler();
  const store = createSaveStatusStore();
  const onConflict = vi.fn();
  const controller = new AutosaveController<string>({
    value: '',
    version: 1,
    save,
    onConflict,
    scheduler: clock.scheduler,
    store,
  });
  return { clock, store, onConflict, controller };
}

describe('AutosaveController', () => {
  it('starts one 3 s timer at the first edit and does not restart it on later edits', () => {
    const { clock, controller, store } = setup(() => Promise.resolve({ version: 2 }));

    controller.sync('a', 1);
    controller.sync('ab', 1);

    expect(clock.pending.size).toBe(1);
    expect([...clock.pending.values()][0].ms).toBe(3000);
    expect(store.getState()).toBe('unsaved');
  });

  it('sends the latest value with the current version and tracks the version it gets back', async () => {
    const save = vi.fn().mockResolvedValue({ version: 2 });
    const { clock, controller, store } = setup(save);
    controller.sync('a', 1);
    controller.sync('ab', 1);

    clock.fire();
    await vi.waitFor(() => expect(store.getState()).toBe('saved'));
    controller.sync('abc', 1);
    await controller.flush();

    expect(save).toHaveBeenNthCalledWith(1, 'ab', 1);
    expect(save).toHaveBeenNthCalledWith(2, 'abc', 2);
  });

  it('schedules another save when edited while a request is in flight', async () => {
    let finish: (v: { version: number }) => void = () => {};
    const { clock, controller, store } = setup(() => new Promise((resolve) => (finish = resolve)));
    controller.sync('a', 1);
    const flushing = controller.flush();
    controller.sync('ab', 1);

    finish({ version: 2 });
    await flushing;

    expect(store.getState()).toBe('unsaved');
    expect(clock.pending.size).toBe(1);
  });

  it('does not retry after a 409 and calls onConflict once', async () => {
    const save = vi.fn().mockRejectedValue(new ApiRequestError('Stale', 409, 'conflict'));
    const { clock, controller, store, onConflict } = setup(save);
    controller.sync('a', 1);

    await controller.flush();
    controller.sync('ab', 1);
    await controller.flush();

    expect(store.getState()).toBe('conflict');
    expect(save).toHaveBeenCalledTimes(1);
    expect(onConflict).toHaveBeenCalledTimes(1);
    expect(clock.pending.size).toBe(0);
  });

  it('treats a 409 that is not a conflict as a failed save, not a stale version', async () => {
    const save = vi.fn().mockRejectedValue(new ApiRequestError('Other', 409, 'validation'));
    const { controller, store, onConflict } = setup(save);
    controller.sync('a', 1);

    await controller.flush();

    expect(store.getState()).toBe('unsaved');
    expect(onConflict).not.toHaveBeenCalled();
  });

  it('leaves a conflict when the lane passes a new server version', async () => {
    const save = vi
      .fn()
      .mockRejectedValueOnce(new ApiRequestError('Stale', 409, 'conflict'))
      .mockResolvedValue({ version: 6 });
    const { controller, store } = setup(save);
    controller.sync('mine', 1);
    await controller.flush();

    controller.sync('theirs', 5);
    expect(store.getState()).toBe('saved');
    controller.sync('theirs plus', 5);
    await controller.flush();

    expect(save).toHaveBeenLastCalledWith('theirs plus', 5);
  });

  it('keeps the edit and reports unsaved when the network fails', async () => {
    const save = vi
      .fn()
      .mockRejectedValueOnce(new TypeError('Failed to fetch'))
      .mockResolvedValue({ version: 2 });
    const { controller, store } = setup(save);
    controller.sync('a', 1);

    await controller.flush();
    expect(store.getState()).toBe('unsaved');
    await controller.flush();

    expect(store.getState()).toBe('saved');
  });

  it('reports retry when the server refuses the save, and saves again on the next flush', async () => {
    const save = vi
      .fn()
      .mockRejectedValueOnce(new ApiRequestError('Down.', 503, 'internal_error'))
      .mockResolvedValue({ version: 2 });
    const { controller, store } = setup(save);
    controller.sync('a', 1);

    await controller.flush();
    expect(store.getState()).toBe('retry');
    await controller.flush();

    expect(store.getState()).toBe('saved');
    expect(save).toHaveBeenLastCalledWith('a', 1);
  });

  it('reports offline when there is no connection and saves by itself once it is back', async () => {
    const save = vi
      .fn()
      .mockRejectedValueOnce(new TypeError('Failed to fetch'))
      .mockResolvedValue({ version: 2 });
    // The platform reports the connection coming back; this one lets the test say when.
    const host = platform();
    const online = new Set<() => void>();
    configurePlatform({
      ...host,
      onOnline: (listener) => {
        online.add(listener);
        return () => void online.delete(listener);
      },
    });
    const clock = manualScheduler();
    const store = createSaveStatusStore();
    const controller = new AutosaveController<string>({
      value: '',
      version: 1,
      save,
      scheduler: clock.scheduler,
      store,
      isOffline: () => true,
    });
    controller.sync('a', 1);

    await controller.flush();
    expect(store.getState()).toBe('offline');
    online.forEach((listener) => listener());

    await vi.waitFor(() => expect(store.getState()).toBe('saved'));
    expect(save).toHaveBeenCalledTimes(2);
    controller.dispose();
    configurePlatform(host);
  });
  it('drops a forbidden save, reports it, and takes the text the lane puts back as saved (#80)', async () => {
    const save = vi
      .fn()
      .mockRejectedValue(new ApiRequestError('Edit access needed.', 403, 'forbidden'));
    const clock = manualScheduler();
    const store = createSaveStatusStore();
    const putBack = vi.fn();
    const controller = new AutosaveController<string>({
      value: 'old',
      version: 1,
      save,
      onForbidden: putBack,
      scheduler: clock.scheduler,
      store,
    });
    const reported = vi.fn();
    const stop = onForbidden(reported);

    controller.sync('typed', 1);
    await controller.flush();
    expect(store.getState()).toBe('saved');
    expect(putBack).toHaveBeenCalledTimes(1);
    expect(reported).toHaveBeenCalledTimes(1);

    // The lane's revert is the new baseline: nothing to save, no timer.
    controller.sync('old', 1);
    expect(clock.pending.size).toBe(0);
    await controller.flush();
    expect(save).toHaveBeenCalledTimes(1);
    stop();
  });
});
