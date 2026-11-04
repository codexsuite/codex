import type { SpeechEngine } from '~/speech';
import { joinTranscripts } from './segments';

/** Transcribes each recorded segment with the active model, in order, and joins the texts. */
export async function transcribeSegments(
  engine: SpeechEngine,
  files: readonly string[],
  signal?: AbortSignal,
): Promise<string> {
  const texts: string[] = [];
  for (const file of files) texts.push(await engine.transcribe(file, { signal }));
  return joinTranscripts(texts);
}
