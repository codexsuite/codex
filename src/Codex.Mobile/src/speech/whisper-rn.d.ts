// The part of whisper.rn this app uses. whisper.rn's package.json exports map has no "." entry, so the bare name does
// not resolve and `whisper.rn` resolves (under the `react-native` condition) to its TypeScript source, which does
// not type-check under React Native 0.88. This declaration is what `nativeWhisper.ts` is checked against.
declare module 'whisper.rn' {
  export interface TranscribeResult {
    result: string;
    isAborted: boolean;
  }
  export interface WhisperContext {
    transcribe(
      filePath: string,
      options?: { language?: string; onProgress?: (progress: number) => void },
    ): { stop: () => Promise<void>; promise: Promise<TranscribeResult> };
    release(): Promise<void>;
  }
  export function initWhisper(options: { filePath: string }): Promise<WhisperContext>;
}
