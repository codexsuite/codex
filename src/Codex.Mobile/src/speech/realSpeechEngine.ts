import { platform } from '@codex/core/platform';
import { createSpeechEngine } from './createSpeechEngine';
import { nativeSpeechFiles } from './nativeSpeechFiles';
import { nativeWhisper } from './nativeWhisper';
import type { SpeechEngine } from './SpeechEngine';

/** The engine the app uses unless a `SpeechEngineProvider` above supplies another: expo-file-system and whisper.rn. */
export const realSpeechEngine: SpeechEngine = createSpeechEngine({
  files: nativeSpeechFiles,
  whisper: nativeWhisper,
  store: () => platform().persistentStore(),
});
