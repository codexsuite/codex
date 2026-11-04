import type { InstalledSpeechModel, SpeechModel } from './SpeechEngine';

// The pure rules of the speech-models folder: what a partial download is called and which files count as installed.

/** Where an unfinished download is written; renamed to the model's file name once complete. */
export const partialSuffix = '.part';
export const partialFileName = (model: SpeechModel): string => `${model.fileName}${partialSuffix}`;

export interface StoredFile {
  name: string;
  sizeBytes: number;
}

/**
 * The models whose file is in the folder with exactly the catalog's size. A half-written file (or one from an
 * interrupted download that was somehow renamed) has the wrong size and does not count.
 */
export function installedFrom(
  files: readonly StoredFile[],
  catalog: readonly SpeechModel[],
): InstalledSpeechModel[] {
  return catalog.flatMap((model) => {
    const file = files.find((candidate) => candidate.name === model.fileName);
    return file !== undefined && file.sizeBytes === model.sizeBytes
      ? [{ id: model.id, sizeBytes: file.sizeBytes }]
      : [];
  });
}

/** Whisper language for a model: English-only models are told `en`, multilingual ones detect it. */
export const languageFor = (model: SpeechModel): string =>
  model.id.endsWith('.en') ? 'en' : 'auto';

/** The total to show in a progress bar: the server's, or the catalog's when the server sent none (-1). */
export const progressTotal = (reported: number, model: SpeechModel): number =>
  reported > 0 ? reported : model.sizeBytes;
