import type {
  DownloadFileOptions,
  SpeechFiles,
  WhisperRuntime,
  WhisperSession,
  WhisperTranscription,
} from '../speechRuntime';

// In-memory SpeechFiles and WhisperRuntime for the engine's tests (vitest). Nothing here is imported by the app.

export interface PendingDownload {
  url: string;
  name: string;
  options: DownloadFileOptions;
  /** Ends the download with a file of this size. */
  finish(sizeBytes: number): void;
  fail(error: Error): void;
}

export class MemoryFiles implements SpeechFiles {
  readonly sizes = new Map<string, number>();
  readonly pending: PendingDownload[] = [];

  list = () => Promise.resolve([...this.sizes].map(([name, sizeBytes]) => ({ name, sizeBytes })));

  download(url: string, name: string, options: DownloadFileOptions): Promise<void> {
    this.sizes.set(name, 1);
    return new Promise<void>((resolve, reject) => {
      options.signal.addEventListener('abort', () =>
        reject(Object.assign(new Error('Aborted'), { name: 'AbortError' })),
      );
      this.pending.push({
        url,
        name,
        options,
        finish: (sizeBytes) => {
          this.sizes.set(name, sizeBytes);
          resolve();
        },
        fail: reject,
      });
    });
  }

  rename(from: string, to: string) {
    const size = this.sizes.get(from);
    if (size === undefined) return Promise.reject(new Error(`No file ${from}.`));
    this.sizes.delete(from);
    this.sizes.set(to, size);
    return Promise.resolve();
  }

  remove(name: string) {
    this.sizes.delete(name);
    return Promise.resolve();
  }

  uriOf = (name: string) => `file:///models/${name}`;
}

export interface OpenedSession {
  modelUri: string;
  released: boolean;
  /** The audio files transcribed with it, and the options each call got. */
  calls: { audioFileUri: string; language: string }[];
  stopped: number;
}

export class MemoryWhisper implements WhisperRuntime {
  readonly sessions: OpenedSession[] = [];
  /** What the next transcriptions answer; `Error` entries reject. The last entry repeats. */
  answers: (string | Error | 'hang')[] = ['hello'];
  private resolveHanging: (() => void) | null = null;

  open(modelUri: string): Promise<WhisperSession> {
    const opened: OpenedSession = { modelUri, released: false, calls: [], stopped: 0 };
    this.sessions.push(opened);
    return Promise.resolve({
      transcribe: (audioFileUri, { language }): WhisperTranscription => {
        opened.calls.push({ audioFileUri, language });
        const answer = this.answers.length > 1 ? this.answers.shift()! : this.answers[0]!;
        if (answer === 'hang') {
          return {
            promise: new Promise((resolve) => {
              this.resolveHanging = () => resolve({ text: '', aborted: true });
            }),
            stop: () => {
              opened.stopped += 1;
              this.resolveHanging?.();
              return Promise.resolve();
            },
          };
        }
        return {
          promise:
            answer instanceof Error
              ? Promise.reject(answer)
              : Promise.resolve({ text: answer, aborted: false }),
          stop: () => Promise.resolve(),
        };
      },
      release: () => {
        opened.released = true;
        return Promise.resolve();
      },
    });
  }
}
