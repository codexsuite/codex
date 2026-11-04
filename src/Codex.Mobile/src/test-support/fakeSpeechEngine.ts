import {
  SpeechEngineError,
  type DownloadProgress,
  type ModelDownloadState,
  type InstalledSpeechModel,
  type SpeechDownload,
  type SpeechEngine,
  type TranscribeOptions,
} from '../speech/SpeechEngine';
import { findSpeechModel, speechModelCatalog } from '../speech/speechModelCatalog';

// A SpeechEngine for specs: nothing is downloaded or transcribed, and the spec decides when a download progresses or
// ends. No jest APIs in here, so a `*.test.ts` can use it too.

export interface FakeDownloadControl {
  /** Reports `receivedBytes` of the model's size. */
  progress(receivedBytes: number): void;
  /** Completes the download: the model is installed (and active if none was). */
  finish(): void;
  /** Fails the download with a `download-failed` error. */
  fail(message?: string): void;
}

export interface FakeSpeechEngine extends SpeechEngine {
  /** The audio files `transcribe` was called with, in order. */
  readonly transcribed: string[];
  /** Control over the download of `modelId` that is in progress; throws when none is. */
  downloadOf(modelId: string): FakeDownloadControl;
  /** What `transcribe` returns from now on. */
  setTranscript(text: string): void;
  /** Makes the next `transcribe` reject with a `transcribe-failed` error. */
  failNextTranscription(message?: string): void;
}

export interface FakeSpeechEngineOptions {
  installed?: readonly string[];
  active?: string | null;
  transcript?: string;
}

export function createFakeSpeechEngine(options: FakeSpeechEngineOptions = {}): FakeSpeechEngine {
  const installedIds = new Set(options.installed ?? []);
  let active = options.active ?? null;
  let transcript = options.transcript ?? 'This is the fake transcript.';
  let failure: string | null = null;
  const listeners = new Set<() => void>();
  const downloadListeners = new Set<() => void>();
  const states = new Map<string, ModelDownloadState>();
  const running = new Map<string, SpeechDownload>();
  const callbacks = new Map<string, Set<(progress: DownloadProgress) => void>>();
  const setState = (modelId: string, state: ModelDownloadState | null) => {
    if (state === null) states.delete(modelId);
    else states.set(modelId, state);
    downloadListeners.forEach((listener) => listener());
  };
  const downloads = new Map<string, FakeDownloadControl>();
  const transcribed: string[] = [];
  const notify = () => listeners.forEach((listener) => listener());

  const requireModel = (modelId: string) => {
    const model = findSpeechModel(modelId);
    if (!model) throw new SpeechEngineError('not-installed', `No model ${modelId} in the catalog.`);
    return model;
  };

  return {
    transcribed,
    catalog: () => speechModelCatalog,
    installed: () =>
      Promise.resolve(
        [...installedIds].map((id): InstalledSpeechModel => ({
          id,
          sizeBytes: requireModel(id).sizeBytes,
        })),
      ),
    download(modelId, onProgress: (progress: DownloadProgress) => void): SpeechDownload {
      const model = requireModel(modelId);
      const existing = running.get(modelId);
      if (existing) {
        callbacks.get(modelId)?.add(onProgress);
        return existing;
      }
      callbacks.set(modelId, new Set([onProgress]));
      let settle!: { resolve: () => void; reject: (error: SpeechEngineError) => void };
      const done = new Promise<void>((resolve, reject) => (settle = { resolve, reject }));
      const end = () => {
        downloads.delete(modelId);
        running.delete(modelId);
        callbacks.delete(modelId);
      };
      setState(modelId, { status: 'downloading', receivedBytes: 0, totalBytes: model.sizeBytes });
      downloads.set(modelId, {
        progress: (receivedBytes) => {
          const progress = { receivedBytes, totalBytes: model.sizeBytes };
          setState(modelId, { status: 'downloading', ...progress });
          callbacks.get(modelId)?.forEach((callback) => callback(progress));
        },
        finish() {
          end();
          installedIds.add(modelId);
          active ??= modelId;
          setState(modelId, null);
          settle.resolve();
          notify();
        },
        fail(message = 'The download failed.') {
          end();
          setState(modelId, { status: 'failed', message });
          settle.reject(new SpeechEngineError('download-failed', message));
        },
      });
      const handle: SpeechDownload = {
        done,
        cancel() {
          end();
          setState(modelId, null);
          settle.reject(new SpeechEngineError('cancelled', 'The download was cancelled.'));
          return Promise.resolve();
        },
      };
      running.set(modelId, handle);
      return handle;
    },
    downloads: () => Object.fromEntries(states),
    subscribeDownloads(listener) {
      downloadListeners.add(listener);
      return () => void downloadListeners.delete(listener);
    },
    cancelDownload: async (modelId) => void (await running.get(modelId)?.cancel()),
    delete(modelId) {
      installedIds.delete(modelId);
      setState(modelId, null);
      if (active === modelId) active = null;
      notify();
      return Promise.resolve();
    },
    activeModelId: () => Promise.resolve(active),
    setActiveModel(modelId) {
      if (!installedIds.has(modelId)) {
        return Promise.reject(
          new SpeechEngineError('not-installed', `${modelId} is not installed.`),
        );
      }
      active = modelId;
      notify();
      return Promise.resolve();
    },
    transcribe(audioFileUri: string, transcribeOptions?: TranscribeOptions) {
      if (active === null) {
        return Promise.reject(
          new SpeechEngineError('no-active-model', 'Choose a speech model first.'),
        );
      }
      if (transcribeOptions?.signal?.aborted) {
        return Promise.reject(new SpeechEngineError('cancelled', 'Transcription was cancelled.'));
      }
      transcribed.push(audioFileUri);
      if (failure !== null) {
        const message = failure;
        failure = null;
        return Promise.reject(new SpeechEngineError('transcribe-failed', message));
      }
      transcribeOptions?.onProgress?.(100);
      return Promise.resolve(transcript);
    },
    subscribe(listener) {
      listeners.add(listener);
      return () => void listeners.delete(listener);
    },
    downloadOf(modelId) {
      const control = downloads.get(modelId);
      if (!control) throw new Error(`No download of ${modelId} is in progress.`);
      return control;
    },
    setTranscript: (text) => void (transcript = text),
    failNextTranscription: (message = 'Transcription failed.') => void (failure = message),
  };
}
