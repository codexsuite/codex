import type { KeyValueStore } from '@codex/core/platform';
import {
  SpeechEngineError,
  type DownloadProgress,
  type ModelDownloadState,
  type SpeechDownload,
  type SpeechEngine,
  type SpeechModel,
  type TranscribeOptions,
} from './SpeechEngine';
import { findSpeechModel, speechModelCatalog } from './speechModelCatalog';
import {
  installedFrom,
  languageFor,
  partialFileName,
  partialSuffix,
  progressTotal,
} from './speechPaths';
import type { SpeechFiles, WhisperRuntime, WhisperSession } from './speechRuntime';

export const activeModelKey = 'codex.speech.active-model';

export interface SpeechEngineDeps {
  files: SpeechFiles;
  whisper: WhisperRuntime;
  /** Read when needed, not when the engine is made (the platform is configured at start-up). */
  store: () => KeyValueStore;
}

const isAbort = (error: unknown): boolean => error instanceof Error && error.name === 'AbortError';

/** The on-device engine: Hugging Face downloads into a folder, one whisper context for the active model. */
export function createSpeechEngine({ files, whisper, store }: SpeechEngineDeps): SpeechEngine {
  const listeners = new Set<() => void>();
  const notify = () => listeners.forEach((listener) => listener());
  const downloads = new Map<string, SpeechDownload>();
  const progressCallbacks = new Map<string, Set<(p: DownloadProgress) => void>>();
  const downloadStates = new Map<string, ModelDownloadState>();
  const downloadListeners = new Set<() => void>();
  let swept = false;
  let session: { modelId: string; ready: Promise<WhisperSession> } | null = null;

  // A killed app can leave `.part` files of downloads nobody is running; clear them once, the first time we look.
  const sweepPartials = async (listed: { name: string }[]) => {
    if (swept || downloads.size > 0) return;
    swept = true;
    await Promise.all(
      listed
        .filter((file) => file.name.endsWith(partialSuffix))
        .map((file) => files.remove(file.name)),
    );
  };

  const installed = async () => {
    const listed = await files.list();
    await sweepPartials(listed).catch(() => undefined);
    return installedFrom(listed, speechModelCatalog);
  };
  const isInstalled = async (modelId: string) => (await installed()).some((m) => m.id === modelId);

  const requireModel = (modelId: string): SpeechModel => {
    const model = findSpeechModel(modelId);
    if (model === undefined) {
      throw new SpeechEngineError('not-installed', `There is no speech model ${modelId}.`);
    }
    return model;
  };

  const releaseSession = async () => {
    const current = session;
    session = null;
    if (current !== null) await (await current.ready.catch(() => null))?.release();
  };

  const activeModelId = async (): Promise<string | null> => {
    const stored = store().getItem(activeModelKey);
    return stored !== null && (await isInstalled(stored)) ? stored : null;
  };

  const setActive = async (modelId: string) => {
    if (session !== null && session.modelId !== modelId) await releaseSession();
    store().setItem(activeModelKey, modelId);
  };

  function setDownloadState(modelId: string, state: ModelDownloadState | null) {
    if (state === null) downloadStates.delete(modelId);
    else downloadStates.set(modelId, state);
    downloadListeners.forEach((listener) => listener());
  }

  function download(modelId: string, onProgress: (p: DownloadProgress) => void): SpeechDownload {
    const model = requireModel(modelId);
    const running = downloads.get(modelId);
    if (running !== undefined) {
      progressCallbacks.get(modelId)?.add(onProgress);
      return running;
    }

    const abort = new AbortController();
    const partial = partialFileName(model);
    const callbacks = new Set([onProgress]);
    progressCallbacks.set(modelId, callbacks);
    setDownloadState(modelId, {
      status: 'downloading',
      receivedBytes: 0,
      totalBytes: model.sizeBytes,
    });
    const done = (async () => {
      try {
        if (await isInstalled(modelId)) {
          setDownloadState(modelId, null);
          return;
        }
        await files.download(model.url, partial, {
          signal: abort.signal,
          onProgress: (bytesWritten, totalBytes) => {
            const progress = {
              receivedBytes: bytesWritten,
              totalBytes: progressTotal(totalBytes, model),
            };
            setDownloadState(modelId, { status: 'downloading', ...progress });
            callbacks.forEach((callback) => callback(progress));
          },
        });
        const written = (await files.list()).find((file) => file.name === partial);
        if (written?.sizeBytes !== model.sizeBytes) {
          throw new SpeechEngineError(
            'download-failed',
            `The download of ${model.name} was incomplete. Try again.`,
          );
        }
        await files.rename(partial, model.fileName);
        if ((await activeModelId()) === null) await setActive(modelId);
        setDownloadState(modelId, null);
        notify();
      } catch (error) {
        await files.remove(partial).catch(() => undefined);
        const failure =
          error instanceof SpeechEngineError
            ? error
            : abort.signal.aborted || isAbort(error)
              ? new SpeechEngineError('cancelled', 'The download was cancelled.')
              : new SpeechEngineError(
                  'download-failed',
                  error instanceof Error ? error.message : 'The download failed.',
                );
        setDownloadState(
          modelId,
          failure.code === 'cancelled' ? null : { status: 'failed', message: failure.message },
        );
        throw failure;
      } finally {
        downloads.delete(modelId);
        progressCallbacks.delete(modelId);
      }
    })();
    const handle: SpeechDownload = {
      done,
      async cancel() {
        abort.abort();
        await done.catch(() => undefined);
      },
    };
    downloads.set(modelId, handle);
    return handle;
  }

  async function transcribe(audioFileUri: string, options: TranscribeOptions = {}) {
    const modelId = await activeModelId();
    if (modelId === null) {
      throw new SpeechEngineError('no-active-model', 'Choose a speech model first.');
    }
    const model = requireModel(modelId);
    if (options.signal?.aborted) {
      throw new SpeechEngineError('cancelled', 'Transcription was cancelled.');
    }
    if (session?.modelId !== modelId) {
      await releaseSession();
      const ready = whisper.open(files.uriOf(model.fileName));
      session = { modelId, ready };
    }
    const current = session;
    try {
      const opened = await current.ready;
      if (options.signal?.aborted) {
        throw new SpeechEngineError('cancelled', 'Transcription was cancelled.');
      }
      const running = opened.transcribe(audioFileUri, {
        language: languageFor(model),
        onProgress: options.onProgress,
      });
      const onAbort = () => void running.stop();
      options.signal?.addEventListener('abort', onAbort);
      try {
        const { text, aborted } = await running.promise;
        if (aborted) throw new SpeechEngineError('cancelled', 'Transcription was cancelled.');
        return text.trim();
      } finally {
        options.signal?.removeEventListener('abort', onAbort);
      }
    } catch (error) {
      if (error instanceof SpeechEngineError) throw error;
      // Drop the context so the next attempt opens a fresh one; releasing it frees the model in native memory (a context
      // that failed to open has nothing to release).
      if (session === current) await releaseSession();
      throw new SpeechEngineError(
        'transcribe-failed',
        error instanceof Error ? error.message : 'The recording could not be transcribed.',
      );
    }
  }

  return {
    catalog: () => speechModelCatalog,
    installed,
    download,
    async delete(modelId) {
      const model = requireModel(modelId);
      await downloads.get(modelId)?.cancel();
      setDownloadState(modelId, null);
      if (session?.modelId === modelId) await releaseSession();
      await files.remove(model.fileName);
      await files.remove(partialFileName(model));
      if (store().getItem(activeModelKey) === modelId) store().removeItem(activeModelKey);
      notify();
    },
    activeModelId,
    async setActiveModel(modelId) {
      requireModel(modelId);
      if (!(await isInstalled(modelId))) {
        throw new SpeechEngineError('not-installed', `${modelId} is not on this phone.`);
      }
      await setActive(modelId);
      notify();
    },
    transcribe,
    downloads: () => Object.fromEntries(downloadStates),
    subscribeDownloads(listener) {
      downloadListeners.add(listener);
      return () => void downloadListeners.delete(listener);
    },
    cancelDownload: async (modelId) => void (await downloads.get(modelId)?.cancel()),
    subscribe(listener) {
      listeners.add(listener);
      return () => void listeners.delete(listener);
    },
  };
}
