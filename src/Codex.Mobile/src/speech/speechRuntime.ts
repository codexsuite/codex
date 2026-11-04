// The two native layers the speech engine stands on, as small interfaces. `createSpeechEngine` is written against them;
// `nativeSpeechFiles.ts` (expo-file-system) and `nativeWhisper.ts` (whisper.rn) implement them for the phone, and the
// engine's specs implement them in memory.

export interface DownloadFileOptions {
  onProgress: (bytesWritten: number, totalBytes: number) => void;
  signal: AbortSignal;
}

export interface SpeechFiles {
  /** Every file in the speech-models folder, with its size. */
  list(): Promise<{ name: string; sizeBytes: number }[]>;
  /** Writes `url` to `name` in the folder (replacing a file already there); rejects when aborted. */
  download(url: string, name: string, options: DownloadFileOptions): Promise<void>;
  /** Gives the file `from` the name `to`, replacing `to`. */
  rename(from: string, to: string): Promise<void>;
  /** Removes the file; no error when it is not there. */
  remove(name: string): Promise<void>;
  /** The URI to hand to whisper for a file in the folder. */
  uriOf(name: string): string;
}

export interface WhisperTranscription {
  /** Resolves to the text; `aborted` is true when `stop` ended it early. */
  promise: Promise<{ text: string; aborted: boolean }>;
  stop(): Promise<void>;
}

export interface WhisperSession {
  transcribe(
    audioFileUri: string,
    options: { language: string; onProgress?: (percent: number) => void },
  ): WhisperTranscription;
  release(): Promise<void>;
}

export interface WhisperRuntime {
  /** Loads a model file into memory (slow for big models). */
  open(modelUri: string): Promise<WhisperSession>;
}
