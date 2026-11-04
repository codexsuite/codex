import { useSyncExternalStore } from 'react';
import type { GenerateDraftRequest } from '@codex/core/api/write';

/** A generation that is starting, streaming or has failed: it exists only on this screen until it ends. */
export interface LiveDraft {
  target: string;
  sceneId: string;
  status: 'starting' | 'streaming' | 'failed';
  /** Set once the server has created the row. */
  generationId: string | null;
  attempt: number | null;
  text: string;
  error: string | null;
  /** What was asked, so Retry can ask again. */
  request: GenerateDraftRequest;
  templateName: string | null;
}

/** The template, integration and model chosen for a beat (or `scene:<id>`). */
export interface DraftChoice {
  templateId: string | null;
  integrationId: string;
  model: string;
}

// Module-level, not React state: a generation outlives the editor that started it (an apply or a split remounts
// the scene while the next draft is already streaming).
const lives = new Map<string, LiveDraft>();
const choices = new Map<string, DraftChoice>();
const aborts = new Map<string, AbortController>();
const listeners = new Set<() => void>();
let version = 0;

function publish() {
  version += 1;
  listeners.forEach((listener) => listener());
}

function subscribe(listener: () => void) {
  listeners.add(listener);
  return () => listeners.delete(listener);
}

export const draftStore = {
  setLive(draft: LiveDraft) {
    lives.set(draft.target, draft);
    publish();
  },
  patchLive(target: string, patch: Partial<LiveDraft>) {
    const current = lives.get(target);
    if (current) {
      lives.set(target, { ...current, ...patch });
      publish();
    }
  },
  clearLive(target: string) {
    aborts.delete(target);
    if (lives.delete(target)) publish();
  },
  choice: (target: string) => choices.get(target),
  setChoice(target: string, choice: DraftChoice) {
    choices.set(target, choice);
    publish();
  },
  controller(target: string): AbortController {
    const controller = new AbortController();
    aborts.set(target, controller);
    return controller;
  },
  stop(target: string) {
    aborts.get(target)?.abort();
  },
};

export function useLiveDraft(target: string): LiveDraft | undefined {
  useSyncExternalStore(subscribe, () => version);
  return lives.get(target);
}

export function useDraftChoice(target: string): DraftChoice | undefined {
  useSyncExternalStore(subscribe, () => version);
  return choices.get(target);
}
