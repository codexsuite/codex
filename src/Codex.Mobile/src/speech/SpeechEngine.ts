// The on-device speech-to-text contract (#35 M2). The recording sheet and the Speech models screen are written against
// this interface and never import whisper.rn, expo-file-system or expo-audio's file API directly; the real
// implementation (`realSpeechEngine.ts`) and the fake the specs use (`test-support/fakeSpeechEngine.ts`) both satisfy it.

/** One downloadable model (a Whisper ggml file). Static: see `speechModelCatalog.ts`. */
export interface SpeechModel {
  /** Stable id, also the file's base name: `tiny.en` for `ggml-tiny.en.bin`. */
  id: string;
  /** What the screen calls it: `Tiny (English)`. */
  name: string;
  /** One line on the trade-off: `Fastest, least accurate.` */
  description: string;
  /** The file's exact size, for the progress bar and the free-space check. */
  sizeBytes: number;
  url: string;
  /** The file name under the app's speech-models folder. */
  fileName: string;
}

export interface InstalledSpeechModel {
  id: string;
  /** Bytes on disk (equal to the catalog's `sizeBytes` for a complete file). */
  sizeBytes: number;
}

export interface DownloadProgress {
  receivedBytes: number;
  totalBytes: number;
}

export interface SpeechDownload {
  /** Resolves when the file is complete and installed; rejects with a `SpeechEngineError` (`cancelled`, `download-failed`). */
  done: Promise<void>;
  /** Stops the download and removes the partial file; `done` then rejects with `cancelled`. */
  cancel(): Promise<void>;
}

/** A download the engine is running or whose last attempt failed; the Speech models screen reads it back when re-entered. */
export type ModelDownloadState =
  | { status: 'downloading'; receivedBytes: number; totalBytes: number }
  | { status: 'failed'; message: string };

export interface TranscribeOptions {
  /** 0 to 100 while the file is being transcribed. */
  onProgress?: (percent: number) => void;
  /** Aborting rejects the call with `cancelled`. */
  signal?: AbortSignal;
}

export type SpeechEngineErrorCode =
  | 'no-active-model'
  | 'not-installed'
  | 'download-failed'
  | 'cancelled'
  | 'unsupported-audio'
  | 'transcribe-failed';

export class SpeechEngineError extends Error {
  constructor(
    public readonly code: SpeechEngineErrorCode,
    message: string,
  ) {
    super(message);
  }
}

export interface SpeechEngine {
  /** The models that can be downloaded (the catalog). */
  catalog(): readonly SpeechModel[];
  /** The models whose file is complete on this phone. */
  installed(): Promise<readonly InstalledSpeechModel[]>;
  /**
   * Starts downloading `modelId`; `onProgress` is called as bytes arrive. Installing an installed model is a no-op, and
   * asking again while it downloads joins the running download (the new `onProgress` is called too).
   */
  download(modelId: string, onProgress: (progress: DownloadProgress) => void): SpeechDownload;
  /**
   * Each model's download that is running or failed (a cancelled or finished one has no entry). A failure stays until the
   * model is downloaded again or deleted.
   */
  downloads(): Readonly<Record<string, ModelDownloadState>>;
  /** Calls `listener` when a download starts, progresses, ends or fails; returns the unsubscribe. */
  subscribeDownloads(listener: () => void): () => void;
  /** Cancels the running download of `modelId`, if any. */
  cancelDownload(modelId: string): Promise<void>;
  /** Deletes the file. If it was the active model, no model is active afterwards. */
  delete(modelId: string): Promise<void>;
  /** The model used by `transcribe`, or null when none has been chosen. */
  activeModelId(): Promise<string | null>;
  /** Chooses the model; rejects with `not-installed` for one that is not on the phone. */
  setActiveModel(modelId: string): Promise<void>;
  /**
   * Transcribes an audio file with the active model and returns the text, trimmed. The file must be a WAV of 16-bit
   * PCM (the recording sheet records exactly that, see CONTRACTS.md). Rejects with `no-active-model` when none is chosen.
   */
  transcribe(audioFileUri: string, options?: TranscribeOptions): Promise<string>;
  /** Calls `listener` when the installed models or the active model change; returns the unsubscribe. */
  subscribe(listener: () => void): () => void;
}
