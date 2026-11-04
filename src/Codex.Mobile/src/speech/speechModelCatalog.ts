import type { SpeechModel } from './SpeechEngine';

// The Whisper ggml models the Speech models screen offers, from the whisper.cpp repository on Hugging Face. Sizes are
// the files' exact byte counts (checked against the Content-Length of the download URLs). English-only models are
// smaller and more accurate for English than the multilingual model of the same size.
const base = 'https://huggingface.co/ggerganov/whisper.cpp/resolve/main';

const model = (id: string, name: string, description: string, sizeBytes: number): SpeechModel => ({
  id,
  name,
  description,
  sizeBytes,
  url: `${base}/ggml-${id}.bin`,
  fileName: `ggml-${id}.bin`,
});

export const speechModelCatalog: readonly SpeechModel[] = [
  model('tiny.en', 'Tiny (English)', 'Fastest, least accurate.', 77_704_715),
  model('base.en', 'Base (English)', 'A good balance for dictation.', 147_964_211),
  model('small.en', 'Small (English)', 'Most accurate, slower and larger.', 487_614_201),
  model('tiny', 'Tiny (multilingual)', 'Fastest, any language, least accurate.', 77_691_713),
  model('base', 'Base (multilingual)', 'Any language, a good balance.', 147_951_465),
  model('small', 'Small (multilingual)', 'Any language, most accurate.', 487_601_967),
];

/** The model offered first and chosen for the user when they download their first one. */
export const defaultSpeechModelId = 'base.en';

export const findSpeechModel = (id: string): SpeechModel | undefined =>
  speechModelCatalog.find((candidate) => candidate.id === id);
