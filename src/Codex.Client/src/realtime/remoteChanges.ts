import type { QueryClient } from '@tanstack/react-query';
import { clientId } from '@codex/core/api/client';
import type { BookEvent } from '@codex/core/api/events';
import { bookKey } from '@codex/core/api/books';
import { jobKey } from '@codex/core/api/jobs';
import { manuscriptKey, type Manuscript } from '@codex/core/api/write';
import { showToast } from '@codex/core/editing/toastStore';
import { saveStatusStore, type SaveStatusStore } from '@codex/core/editing/saveStatusStore';
import { applyAccessChange } from './accessChanges';

export interface RemoteChangeDeps {
  queryClient: QueryClient;
  saveStatus?: SaveStatusStore;
  toast?: typeof showToast;
  /** This page load's id (api/client.ts); tests pass their own. */
  ownClientId?: string;
  /** Leaves the book for the Library when this user's access is removed (AccessChanged with no role). */
  leave?: () => void;
}

// A ProseSaved is "updated elsewhere" unless this page caused it. Two guards:
// 1. The server stamps every event with the X-Codex-Client of the request that caused it (api/client.ts sends this
//    page's id), so everything this page writes through the API (autosave, KB Make mention, block ops, AI Apply...)
//    carries its own id and is ignored. A write with no such header (MCP, another tab) is remote.
// 2. The version: an autosave puts the version it was given into the manuscript cache before its status leaves
//    "Saving…", so an event whose version the cache already holds is an echo or already seen.
// Only a scene whose manuscript is on screen (the query has observers) counts; another screen will read the current
// prose when it opens. The event can beat the save's own response, so while an edit is unsaved or saving the version
// check waits for the status to settle (saved, conflict, updated elsewhere) and is then made against the cache.
export class RemoteChanges {
  private readonly pending = new Map<string, number>();
  private readonly saveStatus: SaveStatusStore;
  private readonly toast: typeof showToast;
  private unsubscribe: (() => void) | undefined;
  private readonly ownClientId: string;

  constructor(
    private readonly bookId: string,
    private readonly deps: RemoteChangeDeps,
  ) {
    this.saveStatus = deps.saveStatus ?? saveStatusStore;
    this.toast = deps.toast ?? showToast;
    this.ownClientId = deps.ownClientId ?? clientId;
  }

  handle(event: BookEvent): void {
    switch (event.type) {
      case 'proseSaved':
        if (event.clientId === this.ownClientId) break;
        this.proseSaved(event.sceneId, event.version);
        break;
      case 'jobProgress':
        void this.deps.queryClient.invalidateQueries({ queryKey: jobKey(event.jobId) });
        if (event.state === 'done') this.toast({ message: `${capitalise(event.kind)} finished.` });
        if (event.state === 'failed') {
          this.toast({
            message: `${capitalise(event.kind)} failed${event.message ? `: ${event.message}` : '.'}`,
          });
        }
        break;
      case 'bookMetaChanged':
        void this.deps.queryClient.invalidateQueries({ queryKey: bookKey(this.bookId) });
        break;
      case 'accessChanged':
        applyAccessChange(event, {
          queryClient: this.deps.queryClient,
          toast: this.toast,
          leave: this.deps.leave ?? (() => undefined),
        });
        break;
      case 'structureChanged':
        // Delivered, but no screen re-reads its outline for it: a reload does.
        break;
    }
  }

  dispose(): void {
    this.unsubscribe?.();
    this.unsubscribe = undefined;
    this.pending.clear();
    // The banner was about this book's scenes; the next book starts clean.
    if (this.saveStatus.getState() === 'remoteChanged') this.saveStatus.set('saved');
  }

  private proseSaved(sceneId: string, version: number): void {
    const known = this.cachedVersion(sceneId);
    if (known === undefined || known >= version) return;
    this.pending.set(sceneId, Math.max(version, this.pending.get(sceneId) ?? 0));
    if (this.settled()) {
      this.resolvePending();
    } else {
      this.unsubscribe ??= this.saveStatus.subscribe(() => {
        if (this.settled()) this.resolvePending();
      });
    }
  }

  private resolvePending(): void {
    this.unsubscribe?.();
    this.unsubscribe = undefined;
    const changed = [...this.pending].some(([sceneId, version]) => {
      const known = this.cachedVersion(sceneId);
      return known !== undefined && known < version;
    });
    this.pending.clear();
    if (changed) this.saveStatus.setRemoteChange();
  }

  private settled(): boolean {
    const state = this.saveStatus.getState();
    return state !== 'unsaved' && state !== 'saving' && state !== 'retry' && state !== 'offline';
  }

  private cachedVersion(sceneId: string): number | undefined {
    const query = this.deps.queryClient
      .getQueryCache()
      .find<Manuscript>({ queryKey: manuscriptKey(this.bookId) });
    if (query === undefined || query.getObserversCount() === 0) return undefined;
    return query.state.data?.scenes.find((scene) => scene.sceneId === sceneId)?.version;
  }
}

function capitalise(text: string): string {
  return text.charAt(0).toUpperCase() + text.slice(1);
}
