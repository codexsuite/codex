// A stand-in for expo-file-system in specs: `jest.mock('expo-file-system', () => jest.requireActual('./test-support/mockExpoFileSystem'))`.
// Only what the recording sheet touches. `deletedFiles` lists what was deleted; `staleFiles` are files "left behind" in
// the recordings folder by an earlier session.

export const deletedFiles: string[] = [];
export const staleFiles: string[] = [];

type Part = string | { uri: string };
const join = (parts: Part[]) =>
  parts
    .map((part) => (typeof part === 'string' ? part : part.uri))
    .join('/')
    .replace(/\/+$/, '');

export class File {
  readonly uri: string;
  constructor(...parts: Part[]) {
    this.uri = join(parts);
  }
  get exists() {
    return !deletedFiles.includes(this.uri);
  }
  delete() {
    deletedFiles.push(this.uri);
  }
}

export class Directory {
  readonly uri: string;
  readonly exists = true;
  constructor(...parts: Part[]) {
    this.uri = join(parts);
  }
  create() {}
  list() {
    return staleFiles.map((uri) => new File(uri));
  }
}

export const Paths = { cache: 'file:///cache', document: 'file:///documents' };
