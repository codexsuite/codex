import { QueryClient, QueryObserver, skipToken } from '@tanstack/react-query';
import { describe, expect, it, vi } from 'vitest';
import type { ProseSaved } from '@codex/core/api/events';
import { bookKey } from '@codex/core/api/books';
import { jobKey } from '@codex/core/api/jobs';
import { manuscriptKey, type Manuscript } from '@codex/core/api/write';
import { createSaveStatusStore } from '@codex/core/editing/saveStatusStore';
import { RemoteChanges } from './remoteChanges';

const book = '0199a000-0000-7000-8000-000000000012';
const scene = '0199a000-0000-7000-8000-0000000000a1';

const saved = (version: number): ProseSaved => ({
  type: 'proseSaved',
  bookId: book,
  sceneId: scene,
  version,
  origin: 'editor',
});

const OWN = 'tab-own';

function setup(cachedVersion: number | null = 3, onScreen = true) {
  const queryClient = new QueryClient();
  if (cachedVersion !== null) {
    queryClient.setQueryData<Manuscript>(manuscriptKey(book), {
      bookId: book,
      scenes: [{ sceneId: scene, body: 'Text.', version: cachedVersion }],
    });
  }
  // The Write screen's useQuery: while it is mounted the manuscript has an observer.
  if (onScreen) {
    new QueryObserver(queryClient, {
      queryKey: manuscriptKey(book),
      queryFn: skipToken,
    }).subscribe(() => undefined);
  }
  const saveStatus = createSaveStatusStore();
  const toast = vi.fn();
  const changes = new RemoteChanges(book, { queryClient, saveStatus, toast, ownClientId: OWN });
  // What the saving page does: the cache takes the new version, then the status settles.
  const finishOwnSave = (version: number) => {
    queryClient.setQueryData<Manuscript>(manuscriptKey(book), {
      bookId: book,
      scenes: [{ sceneId: scene, body: 'Text.', version }],
    });
    saveStatus.set('saved');
  };
  return { queryClient, saveStatus, toast, changes, finishOwnSave };
}

describe('RemoteChanges ProseSaved', () => {
  it('marks a version newer than the page holds as updated elsewhere', () => {
    const { changes, saveStatus } = setup(3);

    changes.handle(saved(4));

    expect(saveStatus.getState()).toBe('remoteChanged');
  });

  it('ignores the echo of a version the page already holds', () => {
    const { changes, saveStatus } = setup(3);

    changes.handle(saved(3));

    expect(saveStatus.getState()).toBe('saved');
  });

  it('never reports an event this page caused itself, whatever the cache says', () => {
    const { changes, saveStatus } = setup(3);

    changes.handle({ ...saved(9), clientId: OWN });

    expect(saveStatus.getState()).toBe('saved');
  });

  it('reports an event another tab caused', () => {
    const { changes, saveStatus } = setup(3);

    changes.handle({ ...saved(4), clientId: 'tab-other' });

    expect(saveStatus.getState()).toBe('remoteChanged');
  });

  it('leaves the bar alone when the manuscript is cached but no screen shows it', () => {
    const { changes, saveStatus } = setup(3, false);

    changes.handle(saved(4));

    expect(saveStatus.getState()).toBe('saved');
  });

  it('clears the banner when the book is left', () => {
    const { changes, saveStatus } = setup(3);
    changes.handle(saved(4));

    changes.dispose();

    expect(saveStatus.getState()).toBe('saved');
  });

  it('ignores a scene that is not on screen', () => {
    const { changes, saveStatus } = setup(null);

    changes.handle(saved(4));

    expect(saveStatus.getState()).toBe('saved');
  });

  it('treats the echo of its own save as nothing even when the event beats the response', () => {
    const { changes, saveStatus, finishOwnSave } = setup(3);
    saveStatus.set('saving');

    changes.handle(saved(4));
    expect(saveStatus.getState()).toBe('saving');
    finishOwnSave(4);

    expect(saveStatus.getState()).toBe('saved');
  });

  it('reports a change that arrived during a save once the save has settled without it', () => {
    const { changes, saveStatus, finishOwnSave } = setup(3);
    saveStatus.set('saving');

    changes.handle(saved(5));
    finishOwnSave(4);

    expect(saveStatus.getState()).toBe('remoteChanged');
  });

  it('waits out an unsaved edit', () => {
    const { changes, saveStatus } = setup(3);
    saveStatus.set('unsaved');

    changes.handle(saved(4));
    expect(saveStatus.getState()).toBe('unsaved');
    saveStatus.set('conflict');

    expect(saveStatus.getState()).toBe('conflict');
  });

  it('stops listening to the status once disposed', () => {
    const { changes, saveStatus } = setup(3);
    saveStatus.set('saving');
    changes.handle(saved(4));

    changes.dispose();
    saveStatus.set('saved');

    expect(saveStatus.getState()).toBe('saved');
  });
});

describe('RemoteChanges other events', () => {
  it('toasts a finished job and refreshes its query', () => {
    const { changes, toast, queryClient } = setup();
    const invalidate = vi.spyOn(queryClient, 'invalidateQueries');
    const jobId = '0199a000-0000-7000-8000-0000000000b1';

    changes.handle({
      type: 'jobProgress',
      bookId: book,
      jobId,
      kind: 'export',
      percent: 100,
      state: 'done',
      message: null,
    });

    expect(toast).toHaveBeenCalledExactlyOnceWith({ message: 'Export finished.' });
    expect(invalidate).toHaveBeenCalledWith({ queryKey: jobKey(jobId) });
  });

  it('toasts a failed job with its message and stays quiet while it runs', () => {
    const { changes, toast } = setup();
    const progress = {
      type: 'jobProgress',
      bookId: book,
      jobId: 'j',
      kind: 'export',
      percent: 0,
    } as const;

    changes.handle({ ...progress, state: 'running', message: null });
    changes.handle({ ...progress, state: 'failed', message: 'Tectonic is missing' });

    expect(toast).toHaveBeenCalledExactlyOnceWith({
      message: 'Export failed: Tectonic is missing',
    });
  });

  it('refreshes the book header on a meta change', () => {
    const { changes, queryClient } = setup();
    const invalidate = vi.spyOn(queryClient, 'invalidateQueries');

    changes.handle({ type: 'bookMetaChanged', bookId: book, fields: ['title'] });

    expect(invalidate).toHaveBeenCalledWith({ queryKey: bookKey(book) });
  });
});
