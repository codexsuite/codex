import { filePathOf } from './filePath';
import type { WhisperRuntime } from './speechRuntime';

// WhisperRuntime over whisper.rn (JSI): one context per opened model file.
export const nativeWhisper: WhisperRuntime = {
  async open(modelUri) {
    // Loaded on first use: the native module is not needed until someone transcribes.
    const { initWhisper } = await import('whisper.rn');
    const context = await initWhisper({ filePath: filePathOf(modelUri) });
    return {
      transcribe(audioFileUri, { language, onProgress }) {
        const { stop, promise } = context.transcribe(filePathOf(audioFileUri), {
          language,
          onProgress,
        });
        return {
          stop,
          promise: promise.then(({ result, isAborted }) => ({ text: result, aborted: isAborted })),
        };
      },
      release: () => context.release(),
    };
  },
};
