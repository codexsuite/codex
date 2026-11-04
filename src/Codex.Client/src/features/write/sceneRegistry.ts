import { createContext, useContext } from 'react';

/** What block operations need from a mounted scene editor. */
export interface SceneHandle {
  /** Saves what is unsaved and waits for it; false when it could not be saved. */
  settle(): Promise<boolean>;
  /** The saved body and version (valid after `settle`). */
  saved(): { body: string; version: number };
}

export class SceneRegistry {
  private readonly handles = new Map<string, SceneHandle>();
  private readonly waiting = new Map<string, ((handle: SceneHandle) => void)[]>();

  register(sceneId: string, handle: SceneHandle): () => void {
    this.handles.set(sceneId, handle);
    const waiters = this.waiting.get(sceneId) ?? [];
    this.waiting.delete(sceneId);
    waiters.forEach((resolve) => resolve(handle));
    return () => {
      if (this.handles.get(sceneId) === handle) this.handles.delete(sceneId);
    };
  }

  /** Saves every mounted scene's unsaved text and waits; false when any could not be saved. */
  async settleAll(): Promise<boolean> {
    const results = await Promise.all([...this.handles.values()].map((handle) => handle.settle()));
    return results.every(Boolean);
  }

  get(sceneId: string): SceneHandle | undefined {
    return this.handles.get(sceneId);
  }

  /**
   * The scene's handle once its editor has mounted (at once when it already has), or undefined when it has not
   * mounted within `withinMs` (the scene is not on screen: archived, or outside the scope).
   */
  whenRegistered(sceneId: string, withinMs = 10000): Promise<SceneHandle | undefined> {
    const handle = this.handles.get(sceneId);
    if (handle) return Promise.resolve(handle);
    return new Promise((resolve) => {
      const timer = setTimeout(() => {
        const left = (this.waiting.get(sceneId) ?? []).filter((waiter) => waiter !== done);
        if (left.length === 0) this.waiting.delete(sceneId);
        else this.waiting.set(sceneId, left);
        resolve(undefined);
      }, withinMs);
      const done = (found: SceneHandle) => {
        clearTimeout(timer);
        resolve(found);
      };
      this.waiting.set(sceneId, [...(this.waiting.get(sceneId) ?? []), done]);
    });
  }
}

export const SceneRegistryContext = createContext<SceneRegistry | null>(null);

export function useSceneRegistry(): SceneRegistry {
  const registry = useContext(SceneRegistryContext);
  if (registry === null) throw new Error('Write editors need a <SceneRegistryContext>.');
  return registry;
}
