import { Directory, File, Paths } from 'expo-file-system';
import { normaliseFileUri } from '../speech/filePath';

// Recordings are written to a folder of our own so a crashed session's files can be found and cleared.
const folder = () => {
  const directory = new Directory(Paths.cache, 'codex-recordings');
  if (!directory.exists) directory.create({ intermediates: true, idempotent: true });
  return directory;
};

/** Where segment number `index` of a recording is written. */
export const segmentUri = (index: number): string => new File(folder(), `segment-${index}.wav`).uri;

/** Deletes recorded audio files; a file that is already gone is not an error. */
export function deleteAudioFiles(uris: readonly string[]): void {
  for (const uri of uris) {
    try {
      // expo-audio reports `file:/data/...` on Android; expo-file-system wants `file:///`.
      const file = new File(normaliseFileUri(uri));
      if (file.exists) file.delete();
    } catch {
      // Cache files are cleared by the system eventually; a failed delete must not break closing the sheet.
    }
  }
}

/** Removes recordings an earlier session left behind (a crash, a killed app). Call before a new recording starts. */
export function sweepStaleRecordings(): void {
  try {
    for (const item of folder().list()) item.delete();
  } catch {
    // Nothing to sweep, or the folder could not be read; recording still works.
  }
}
