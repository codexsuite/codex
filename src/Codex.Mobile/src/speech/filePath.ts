// Android's expo-audio reports `file:/data/...` (one slash), expo-file-system `file:///...`; whisper.rn strips only
// `file://`. These make every form one of two canonical shapes.

/** The URI with exactly `file://` and an absolute path: `file:/a`, `file://a` and `file:///a` all become `file:///a`. */
export const normaliseFileUri = (uri: string): string => uri.replace(/^file:\/*/, 'file:///');

/** A plain, decoded filesystem path for native code that takes paths (whisper.rn); other strings pass through. */
export function filePathOf(uri: string): string {
  if (!uri.startsWith('file:')) return uri;
  const path = normaliseFileUri(uri).slice('file://'.length);
  try {
    return decodeURI(path);
  } catch {
    return path;
  }
}
