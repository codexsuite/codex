import { Directory, File, Paths } from 'expo-file-system';
import type { DownloadFileOptions, SpeechFiles } from './speechRuntime';

// SpeechFiles over expo-file-system: the models live in `<documents>/speech-models`.
const folder = () => {
  const directory = new Directory(Paths.document, 'speech-models');
  if (!directory.exists) directory.create({ intermediates: true, idempotent: true });
  return directory;
};

export const nativeSpeechFiles: SpeechFiles = {
  list: () =>
    Promise.resolve(
      folder()
        .list()
        .flatMap((item) =>
          item instanceof File ? [{ name: item.name, sizeBytes: item.size }] : [],
        ),
    ),
  async download(url: string, name: string, { onProgress, signal }: DownloadFileOptions) {
    await File.downloadFileAsync(url, new File(folder(), name), {
      idempotent: true,
      signal,
      onProgress: ({ bytesWritten, totalBytes }) => onProgress(bytesWritten, totalBytes),
    });
  },
  rename(from, to) {
    const target = new File(folder(), to);
    if (target.exists) target.delete();
    new File(folder(), from).rename(to);
    return Promise.resolve();
  },
  remove(name) {
    const file = new File(folder(), name);
    if (file.exists) file.delete();
    return Promise.resolve();
  },
  uriOf: (name) => new File(folder(), name).uri,
};
