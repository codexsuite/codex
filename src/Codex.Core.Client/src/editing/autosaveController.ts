import { platform } from '../platform';
import { ApiRequestError } from '../api/client';
import { reportForbidden } from '../realtime/forbidden';
import { showToast } from './toastStore';
import type { SaveStatusStore } from './saveStatusStore';

export const AUTOSAVE_DELAY_MS = 3000;

// The timer the controller uses; tests pass a manual one.
export interface Scheduler {
  setTimeout(callback: () => void, ms: number): unknown;
  clearTimeout(handle: unknown): void;
}

export const defaultScheduler: Scheduler = {
  setTimeout: (callback, ms) => globalThis.setTimeout(callback, ms),
  clearTimeout: (handle) => globalThis.clearTimeout(handle as ReturnType<typeof setTimeout>),
};

export interface AutosaveOptions<T> {
  value: T;
  version: number;
  save: (value: T, version: number) => Promise<{ version: number }>;
  onConflict?: () => void;
  /** The server refused the save as `forbidden` (403, #80): the edit is dropped; the lane may put its text back. */
  onForbidden?: () => void;
  delayMs?: number;
  scheduler?: Scheduler;
  store: SaveStatusStore;
  // Whether the device has no connection; defaults to the platform's `isOffline`. Tests pass their own.
  isOffline?: () => boolean;
}

// Saves a value 3 s after its first edit and on demand (blur). It knows nothing about React or about what is
// saved: the lane passes the endpoint as `save`.
export class AutosaveController<T> {
  private baseline: T;
  private latest: T;
  private version: number;
  private lastPropVersion: number;
  private timer: unknown = undefined;
  private saving = false;
  private conflicted = false;
  // After a 403 whose lane puts its text back (onForbidden): that value is the baseline, not an edit to save.
  private adoptNext = false;
  private stopListening: (() => void) | undefined;

  constructor(private options: AutosaveOptions<T>) {
    this.baseline = options.value;
    this.latest = options.value;
    this.version = options.version;
    this.lastPropVersion = options.version;
  }

  configure(options: AutosaveOptions<T>) {
    this.options = options;
  }

  // Called with the props on every change. A changed version from outside (a reload after a conflict or a
  // remote change) adopts the server's value as the new baseline; anything else is an edit.
  sync(value: T, version: number) {
    if (version !== this.lastPropVersion) {
      this.lastPropVersion = version;
      if (!this.saving && version !== this.version) {
        this.version = version;
        this.baseline = value;
        this.latest = value;
        this.conflicted = false;
        this.cancelTimer();
        this.options.store.set('saved');
        return;
      }
    }
    if (this.adoptNext) {
      this.adoptNext = false;
      this.baseline = value;
      this.latest = value;
      return;
    }
    this.latest = value;
    if (this.conflicted || this.saving || Object.is(value, this.baseline)) return;
    this.options.store.set('unsaved');
    this.startTimer();
  }

  // Saves now if there is something to save (blur).
  async flush() {
    this.cancelTimer();
    await this.run();
  }

  dispose() {
    this.cancelTimer();
    this.stopListening?.();
    this.stopListening = undefined;
  }

  private startTimer() {
    if (this.timer !== undefined) return;
    const scheduler = this.options.scheduler ?? defaultScheduler;
    this.timer = scheduler.setTimeout(() => {
      this.timer = undefined;
      void this.run();
    }, this.options.delayMs ?? AUTOSAVE_DELAY_MS);
  }

  private cancelTimer() {
    if (this.timer === undefined) return;
    (this.options.scheduler ?? defaultScheduler).clearTimeout(this.timer);
    this.timer = undefined;
  }

  private waitForConnection() {
    if (this.stopListening) return;
    const online = () => {
      this.stopListening?.();
      this.stopListening = undefined;
      void this.run();
    };
    this.stopListening = platform().onOnline(online);
  }

  private async run() {
    if (this.saving || this.conflicted || Object.is(this.latest, this.baseline)) return;
    const { store, save, onConflict } = this.options;
    const sent = this.latest;
    this.saving = true;
    store.set('saving');
    try {
      const result = await save(sent, this.version);
      this.version = result.version;
      this.baseline = sent;
      this.saving = false;
      if (Object.is(this.latest, this.baseline)) {
        store.set('saved');
      } else {
        // Edited while the request was in flight: another round, 3 s from now.
        store.set('unsaved');
        this.startTimer();
      }
    } catch (error) {
      this.saving = false;
      if (error instanceof ApiRequestError && error.status === 409 && error.code === 'conflict') {
        // Stale version: never retried. The lane decides how to reconcile (its onConflict, then a reload).
        this.conflicted = true;
        store.set('conflict');
        onConflict?.();
      } else if (error instanceof ApiRequestError && error.status === 403) {
        // Below the role this needs (#80): never retried. The server's sentence says why; the book's header is re-read,
        // so a role that dropped turns the screen read-only.
        this.baseline = sent;
        store.set('saved');
        showToast({ message: error.message });
        reportForbidden(error);
        if (this.options.onForbidden) {
          this.adoptNext = true;
          this.options.onForbidden();
        }
      } else if (error instanceof ApiRequestError && error.status >= 500) {
        // The server failed (5xx): the edit is kept and the bar offers Retry.
        store.set('retry');
      } else if ((this.options.isOffline ?? (() => platform().isOffline()))()) {
        // No connection: the edit waits and goes by itself when the device is back online.
        this.waitForConnection();
        store.set('offline');
      } else {
        // A request that failed on the way: keep the edit; the next edit or blur tries again.
        store.set('unsaved');
      }
    }
  }
}
