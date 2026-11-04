import { useCallback, useEffect, useState } from 'react';
import type { DownloadProgress } from './SpeechEngine';
import { useSpeechEngine } from './SpeechEngineContext';

export interface SpeechModelsState {
  /** True until the first read of the installed and active models has finished. */
  loading: boolean;
  loadError: string | null;
  installedIds: ReadonlySet<string>;
  activeId: string | null;
  /** Downloads under way (the engine keeps them, so they are still here when the screen is opened again). */
  progress: Readonly<Record<string, DownloadProgress>>;
  /** Why the last download, delete or choice of a model failed. */
  failures: Readonly<Record<string, string>>;
}

export interface SpeechModelsActions {
  download(modelId: string): void;
  cancel(modelId: string): void;
  remove(modelId: string): void;
  choose(modelId: string): void;
  reload(): void;
}

const without = <T>(record: Readonly<Record<string, T>>, key: string): Record<string, T> =>
  Object.fromEntries(Object.entries(record).filter(([name]) => name !== key));

const messageOf = (error: unknown, fallback: string) =>
  error instanceof Error ? error.message : fallback;

const noProgress = () => {};

/** The Speech models screen's state over the injected engine: what is installed and active, and the downloads under way. */
export function useSpeechModels(): SpeechModelsState & SpeechModelsActions {
  const engine = useSpeechEngine();
  const [loading, setLoading] = useState(true);
  const [loadError, setLoadError] = useState<string | null>(null);
  const [installedIds, setInstalledIds] = useState<ReadonlySet<string>>(new Set());
  const [activeId, setActiveId] = useState<string | null>(null);
  const [downloads, setDownloads] = useState(() => engine.downloads());
  const [actionFailures, setActionFailures] = useState<Record<string, string>>({});

  const reload = useCallback(() => {
    Promise.all([engine.installed(), engine.activeModelId()]).then(
      ([installed, active]) => {
        setInstalledIds(new Set(installed.map((model) => model.id)));
        setActiveId(active);
        setLoadError(null);
        setLoading(false);
      },
      (error: unknown) => {
        setLoadError(messageOf(error, 'The speech models could not be read.'));
        setLoading(false);
      },
    );
  }, [engine]);

  useEffect(() => {
    reload();
    const stopModels = engine.subscribe(reload);
    const stopDownloads = engine.subscribeDownloads(() => setDownloads(engine.downloads()));
    setDownloads(engine.downloads());
    return () => {
      stopModels();
      stopDownloads();
    };
  }, [engine, reload]);

  const fail = useCallback(
    (modelId: string, cause: unknown, fallback: string) =>
      setActionFailures((current) => ({ ...current, [modelId]: messageOf(cause, fallback) })),
    [],
  );

  const download = useCallback(
    (modelId: string) => {
      setActionFailures((current) => without(current, modelId));
      try {
        // The engine records progress and failure itself; nothing here needs to wait for the result.
        engine.download(modelId, noProgress).done.catch(() => undefined);
      } catch (error) {
        fail(modelId, error, 'Could not start.');
      }
    },
    [engine, fail],
  );

  const cancel = useCallback((modelId: string) => void engine.cancelDownload(modelId), [engine]);

  const remove = useCallback(
    (modelId: string) => {
      setActionFailures((current) => without(current, modelId));
      engine
        .delete(modelId)
        .then(reload, (error: unknown) => fail(modelId, error, 'The model could not be deleted.'));
    },
    [engine, fail, reload],
  );

  const choose = useCallback(
    (modelId: string) => {
      setActionFailures((current) => without(current, modelId));
      engine
        .setActiveModel(modelId)
        .then(reload, (error: unknown) => fail(modelId, error, 'The model could not be chosen.'));
    },
    [engine, fail, reload],
  );

  const progress: Record<string, DownloadProgress> = {};
  const failures: Record<string, string> = { ...actionFailures };
  for (const [modelId, state] of Object.entries(downloads)) {
    if (state.status === 'downloading') progress[modelId] = state;
    else failures[modelId] = state.message;
  }

  return {
    loading,
    loadError,
    installedIds,
    activeId,
    progress,
    failures,
    download,
    cancel,
    remove,
    choose,
    reload,
  };
}
