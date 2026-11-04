import { AutosaveController, type Scheduler } from '@codex/core/editing/autosaveController';
import { leave, liveAutosaves, type LiveAutosave } from '~/book/liveAutosaves';
import type { SaveSources } from '~/book/saveStatus';

/** Text the server does not hold yet, with the version it was written against. */
export interface UnsavedDraft {
  body: string;
  version: number;
}

export interface SceneSavesDeps {
  /** The open book: live scene controllers are kept per book (`write:<sceneId>`) so a screen coming back adopts them. */
  bookId: string;
  /** The open book's save-status sources (the book bar's dot shows the worst of them); each scene is `write:<id>`. */
  sources: SaveSources;
  /** `PUT /api/scenes/{id}/prose` with the version the controller holds. */
  saveProse(sceneId: string, body: string, version: number): Promise<{ version: number }>;
  /** A save landed: the cache holds `body` at `version` now. */
  onSaved(sceneId: string, body: string, version: number): void;
  /**
   * Defensive: the controller reports a stale version (409). The prose PUT is last-write-wins and answers 200, so the
   * server does not send this today; the caller reloads the scene from the server and calls `reset`.
   */
  onConflict(sceneId: string): void;
  /** The server refused a save as forbidden (403); the caller puts the saved text back and calls `reset`. */
  onForbidden(sceneId: string): void;
  /** Keeps (or, with null, forgets) text that could not be saved, so leaving the screen does not lose it. */
  keepDraft(sceneId: string, draft: UnsavedDraft | null): void;
  /** The kept text for a scene, if it was written against `serverVersion`. */
  readDraft(sceneId: string, serverVersion: number): UnsavedDraft | undefined;
  scheduler?: Scheduler;
  isOffline?: () => boolean;
}

/** What the server is known to hold for a scene, and the save in flight. */
interface Track {
  body: string;
  version: number;
  inflight: Promise<unknown> | null;
  /** The last attempt to save was refused or failed (as opposed to text changing while it was in flight). */
  failed: boolean;
  /** The newest text the page reported. */
  latest: string;
}

interface Saving {
  controller: AutosaveController<string>;
  /** The version the page was loaded with: the controller's "props" version, which only a reload changes. */
  loadedVersion: number;
  track: Track;
  /** The controller's place in the book's registry of live autosaves. */
  live: LiveAutosave;
  /** The screen's `SceneSaves` that drives it now; one that adopted it earlier and has since left no longer does. */
  owner: SceneSaves;
}

const isDirty = (entry: Saving) => entry.track.latest !== entry.track.body;

/**
 * One `AutosaveController` per scene. It is fed the page's `change` messages, saves through the core write API, and
 * remembers each scene's last saved body and version (what a block operation sends).
 *
 * A scene with text the server does not hold is never dropped: when the screen goes away its controller stays alive in
 * the book's registry while it can still send (it waits for the connection, or retries after 5, 15 and 45 s), and the
 * next `open` of that scene, from a new `SceneSaves`, adopts that controller and gives the pending text back. When the
 * retries run out the controller retires and the text stays as a draft.
 */
export class SceneSaves {
  private readonly scenes = new Map<string, Saving>();
  constructor(private readonly deps: SceneSavesDeps) {}

  private key(sceneId: string) {
    return `write:${sceneId}`;
  }

  private store(sceneId: string) {
    return this.deps.sources.store(this.key(sceneId));
  }

  /** A controller left alive by an earlier screen for this scene, taken over by this one. */
  private adopt(sceneId: string): Saving | undefined {
    const live = liveAutosaves.get(this.deps.bookId, this.key(sceneId));
    if (!live) return undefined;
    liveAutosaves.claim(live);
    const entry = live.data as Saving;
    // Its callbacks belonged to the screen that left; this screen's take over.
    entry.controller.configure(this.optionsFor(sceneId, entry.track, entry.loadedVersion));
    entry.owner = this;
    this.scenes.set(sceneId, entry);
    return entry;
  }

  /** True (and forgotten here) when a newer `SceneSaves` has taken the scene over since this one held it. */
  private lost(sceneId: string, entry: Saving): boolean {
    if (entry.owner === this) return false;
    if (this.scenes.get(sceneId) === entry) this.scenes.delete(sceneId);
    return true;
  }

  private retire(sceneId: string, entry: Saving): void {
    entry.controller.dispose();
    this.deps.sources.release(this.key(sceneId), entry.live.store);
    liveAutosaves.remove(this.deps.bookId, this.key(sceneId), entry.live);
  }

  /**
   * What the page should show for a scene it is about to be loaded with. A scene holding unsaved text keeps it (and its
   * controller); otherwise saving starts afresh from this server copy, with a kept draft laid over it when one fits.
   */
  open(
    sceneId: string,
    server: { body: string; version: number },
  ): { body: string; version: number } {
    const existing = this.scenes.get(sceneId) ?? this.adopt(sceneId);
    if (existing && isDirty(existing)) {
      return { body: existing.track.latest, version: existing.track.version };
    }
    this.begin(sceneId, server.body, server.version);
    const draft = this.deps.readDraft(sceneId, server.version);
    if (draft && draft.body !== server.body) {
      this.change(sceneId, draft.body);
      return { body: draft.body, version: server.version };
    }
    return server;
  }

  /** The server's copy replaced what the page had (after a refusal or a block operation). */
  reset(sceneId: string, body: string, version: number): void {
    this.begin(sceneId, body, version);
  }

  /** Stops saving the clean scenes not in `keep` (the chapter changed). Scenes with unsaved text stay. */
  retain(keep: readonly string[]): void {
    for (const [id, entry] of [...this.scenes]) {
      if (this.lost(id, entry) || keep.includes(id) || isDirty(entry)) continue;
      this.retire(id, entry);
      this.scenes.delete(id);
    }
  }

  /** The page reported new text for a scene. */
  change(sceneId: string, markdown: string): void {
    const entry = this.scenes.get(sceneId);
    if (!entry) return;
    entry.track.latest = markdown;
    entry.controller.sync(markdown, entry.loadedVersion);
  }

  /** The last body and version the server is known to hold for a scene. */
  saved(sceneId: string): { body: string; version: number } | undefined {
    const entry = this.scenes.get(sceneId);
    return entry && { body: entry.track.body, version: entry.track.version };
  }

  /** Whether this scene is being saved from here. */
  has(sceneId: string): boolean {
    return this.scenes.has(sceneId);
  }

  /** Whether this scene holds text the server does not have. */
  unsaved(sceneId: string): boolean {
    const entry = this.scenes.get(sceneId);
    return entry !== undefined && isDirty(entry);
  }

  /**
   * Saves what is pending and waits for it, for the given scenes (default: all). True when nothing is left waiting; false
   * when a save conflicted, was refused or is waiting for the connection (that text is kept as a draft).
   */
  async settle(sceneIds?: readonly string[]): Promise<boolean> {
    let clean = true;
    for (const [id, entry] of [...this.scenes]) {
      if (sceneIds && !sceneIds.includes(id)) continue;
      if (this.lost(id, entry)) continue;
      // Text typed while a save was in flight waits for the next round, so go again while something is unsaved.
      for (let round = 0; round < 3; round++) {
        await entry.controller.flush();
        if (entry.track.inflight) await entry.track.inflight;
        if (entry.track.failed || this.store(id).getState() !== 'unsaved') break;
      }
      // Adopted by a newer screen while this one waited: it is that screen's now.
      if (this.lost(id, entry)) continue;
      if (this.store(id).getState() !== 'saved') clean = false;
      if (isDirty(entry))
        this.deps.keepDraft(id, { body: entry.track.latest, version: entry.track.version });
    }
    return clean;
  }

  /** Marks a scene's save state (a block operation that conflicted). */
  markConflict(sceneId: string): void {
    this.store(sceneId).set('conflict');
  }

  /**
   * The screen is going away. Clean scenes are retired. A scene with unsaved text keeps its controller in the book's
   * registry (see `leave`): it goes on sending while it can and retires after its retries, and its text is kept as a
   * draft for the next time the scene is opened.
   */
  dispose(): void {
    for (const [id, entry] of [...this.scenes]) {
      if (this.lost(id, entry)) continue;
      if (isDirty(entry)) {
        this.deps.keepDraft(id, { body: entry.track.latest, version: entry.track.version });
        leave(entry.live, {
          settle: async () => {
            await entry.controller.flush();
            await entry.track.inflight;
          },
          retire: () => this.retire(id, entry),
        });
      } else {
        this.retire(id, entry);
      }
    }
    this.scenes.clear();
  }

  private optionsFor(sceneId: string, track: Track, loadedVersion: number) {
    return {
      value: track.body,
      version: loadedVersion,
      store: this.store(sceneId),
      scheduler: this.deps.scheduler,
      isOffline: this.deps.isOffline,
      save: (value: string, savedVersion: number) => this.save(sceneId, track, value, savedVersion),
      onConflict: () => this.deps.onConflict(sceneId),
      onForbidden: () => this.deps.onForbidden(sceneId),
    };
  }

  private begin(sceneId: string, body: string, version: number): void {
    const old = this.scenes.get(sceneId);
    if (old) this.retire(sceneId, old);
    else {
      const left = liveAutosaves.get(this.deps.bookId, this.key(sceneId));
      if (left) {
        liveAutosaves.claim(left);
        this.retire(sceneId, left.data as Saving);
      }
    }
    const track: Track = { body, version, inflight: null, failed: false, latest: body };
    const controller = new AutosaveController<string>(this.optionsFor(sceneId, track, version));
    const live: LiveAutosave = {
      controller,
      store: this.store(sceneId),
      latest: { value: body, version },
      claims: 0,
    };
    const entry: Saving = { controller, loadedVersion: version, track, live, owner: this };
    live.data = entry;
    liveAutosaves.add(this.deps.bookId, this.key(sceneId), live);
    this.scenes.set(sceneId, entry);
    this.store(sceneId).set('saved');
  }

  private save(sceneId: string, track: Track, body: string, version: number) {
    const request = this.deps.saveProse(sceneId, body, version).then(
      (result) => {
        track.failed = false;
        track.body = body;
        track.version = result.version;
        this.deps.onSaved(sceneId, body, result.version);
        if (track.latest === body) this.deps.keepDraft(sceneId, null);
        return result;
      },
      (error: unknown) => {
        track.failed = true;
        throw error;
      },
    );
    const tracked = request.then(
      () => undefined,
      () => undefined,
    );
    track.inflight = tracked;
    void tracked.then(() => {
      if (track.inflight === tracked) track.inflight = null;
    });
    return request;
  }
}
