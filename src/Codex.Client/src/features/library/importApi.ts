import { useQuery, type QueryClient } from '@tanstack/react-query';
import { api } from '@codex/core/api/client';

// Mirrors Codex.Contracts.Import.
export interface ImportCount {
  item: string;
  count: number;
  destination: string;
}

export interface ImportSkipped {
  path: string;
  reason: string;
}

export interface ImportPreview {
  source: string;
  bookTitle: string;
  fileCount: number;
  words: number;
  creates: ImportCount[];
  skipped: ImportSkipped[];
  /** The images the import places (paths as uploaded), for a source that takes them: send exactly these. */
  images?: string[];
}

export interface ImportSummary {
  importId: string;
  bookId: string;
  source: string;
  fileName: string;
  acts: number;
  chapters: number;
  scenes: number;
  words: number;
  entries: number;
  /** Only a Codex backup has sources and labels. */
  sources?: number;
  citedSources?: number;
  labels?: number;
  skipped: ImportSkipped[];
}

export interface BookImport {
  banner: ImportSummary | null;
  canUndo: boolean;
}

/** Mirrors Codex.Application.Features.Import.ImportLimits. */
export const importLimits = {
  maxFiles: 2000,
  maxTotalBytes: 25 * 1024 * 1024,
  maxFileBytes: 2 * 1024 * 1024,
  maxImageBytes: 25 * 1024 * 1024,
  maxImageTotalBytes: 4 * 1024 * 1024 * 1024,
} as const;

/** Sources whose import also uploads the images the text places (ImportLimits.TakesImages). */
export const imageSources: readonly string[] = ['pandoc'];

/** What one import sends: the Markdown files to read, and the names of those it leaves out. */
export interface ImportSelection {
  send: File[];
  notMarkdown: string[];
  tooLarge: string[];
  bytes: number;
}

const pathOf = (file: File) => file.webkitRelativePath || file.name;

/** The text files a folder source reads: Markdown, plus a pandoc defaults .yaml for the pandoc source. */
const isText = (path: string, source: string) =>
  /\.md$/i.test(path) || (source === 'pandoc' && /\.ya?ml$/i.test(path));

/** Only Markdown files under the size cap are uploaded; images and the rest are only named, so they show as skipped. */
export function selectImportFiles(files: readonly File[], source = 'markdown'): ImportSelection {
  const selection: ImportSelection = { send: [], notMarkdown: [], tooLarge: [], bytes: 0 };
  for (const file of files) {
    if (!isText(pathOf(file), source)) selection.notMarkdown.push(pathOf(file));
    else if (file.size > importLimits.maxFileBytes) selection.tooLarge.push(pathOf(file));
    else {
      selection.send.push(file);
      selection.bytes += file.size;
    }
  }
  return selection;
}

/**
 * The import's upload for a source that takes images: the preview's selection plus the images it listed (sent rather
 * than named). An image over the asset cap is named as too large instead; the server lists it as skipped.
 */
export function withImages(
  selection: ImportSelection,
  files: readonly File[],
  images: readonly string[],
): ImportSelection & { imageBytes: number } {
  const wanted = new Set(images);
  const picked = files.filter((f) => wanted.has(pathOf(f)));
  const send = picked.filter((f) => f.size <= importLimits.maxImageBytes);
  const tooLarge = picked.filter((f) => f.size > importLimits.maxImageBytes).map(pathOf);
  const moved = new Set(picked.map(pathOf));
  return {
    send: [...selection.send, ...send],
    notMarkdown: selection.notMarkdown.filter((p) => !moved.has(p)),
    tooLarge: [...selection.tooLarge, ...tooLarge],
    bytes: selection.bytes,
    imageBytes: send.reduce((sum, f) => sum + f.size, 0),
  };
}

/** The images' own cap, checked before uploading them (the server refuses past it). */
export function checkImages(selection: { imageBytes: number }): string | null {
  if (selection.imageBytes > importLimits.maxImageTotalBytes)
    return `The images this book uses are ${Math.ceil(selection.imageBytes / (1024 * 1024 * 1024))} GB; an import can upload at most 4 GB of images.`;
  return null;
}

/** A single-file source (a Novelcrafter .zip, a .docx): the file is sent whole and the server reads inside it. */
export function selectImportArchive(files: readonly File[]): ImportSelection {
  const send = files.slice(0, 1);
  return { send, notMarkdown: [], tooLarge: [], bytes: send.reduce((sum, f) => sum + f.size, 0) };
}

/** The size cap for a single file, checked before uploading (the server refuses past it). */
export function checkArchive(selection: ImportSelection): string | null {
  if (selection.send.length === 0) return 'Pick a file to import.';
  if (selection.bytes > importLimits.maxTotalBytes)
    return `This file is ${Math.ceil(selection.bytes / (1024 * 1024))} MB; an import can be at most 25 MB.`;
  return null;
}

/** The caps checked before uploading (the server refuses past them, and resets the connection well past them). */
export function checkSelection(selection: ImportSelection): string | null {
  const count = selection.send.length + selection.notMarkdown.length + selection.tooLarge.length;
  if (count > importLimits.maxFiles)
    return `An import can hold at most ${importLimits.maxFiles} files; this folder has ${count}. Pick the manuscript folder itself.`;
  if (selection.bytes > importLimits.maxTotalBytes)
    return `The Markdown in this folder is ${Math.ceil(selection.bytes / (1024 * 1024))} MB; an import can be at most 25 MB.`;
  if (selection.send.length === 0) return 'This folder has no Markdown (.md) files to import.';
  return null;
}

/** The upload the importer reads: the source, one part per file named by its path in the folder, and the left-out names. */
export function importForm(source: string, selection: ImportSelection): FormData {
  const form = new FormData();
  form.append('source', source);
  for (const file of selection.send) form.append('files', file, pathOf(file));
  for (const path of selection.notMarkdown) form.append('notMarkdown', path);
  for (const path of selection.tooLarge) form.append('tooLarge', path);
  return form;
}

export const importApi = {
  preview: (form: FormData) => api.upload<ImportPreview>('/api/imports/preview', form),
  run: (form: FormData) => api.upload<ImportSummary>('/api/imports', form),
  dismiss: (importId: string) => api.post<void>(`/api/imports/${importId}/dismiss`),
  undo: (importId: string) => api.post<void>(`/api/imports/${importId}/undo`),
};

export const bookImportKey = (bookId: string) => ['book-import', bookId] as const;

export function useBookImport(bookId: string) {
  return useQuery({
    queryKey: bookImportKey(bookId),
    queryFn: ({ signal }) => api.get<BookImport>(`/api/books/${bookId}/import`, { signal }),
    enabled: bookId !== '',
  });
}

export function clearBookImport(queryClient: QueryClient, bookId: string) {
  queryClient.setQueryData<BookImport>(bookImportKey(bookId), { banner: null, canUndo: false });
}

const plural = (n: number, one: string, many = `${one}s`) =>
  `${n.toLocaleString('en-US')} ${n === 1 ? one : many}`;

/** "2 acts, 3 chapters, 4 scenes and 1,200 words, plus 3 Knowledge base entries." (a backup adds ", 12 sources (11 cited) and 4 labels") */
export function describeImport(s: ImportSummary): string {
  const entries =
    s.entries > 0
      ? `, plus ${plural(s.entries, 'Knowledge base entry', 'Knowledge base entries')}`
      : '';
  const sources = s.sources
    ? `, ${plural(s.sources, 'source')} (${s.citedSources ?? 0} cited)`
    : '';
  const labels = s.labels ? ` and ${plural(s.labels, 'label')}` : '';
  return `${plural(s.acts, 'act')}, ${plural(s.chapters, 'chapter')}, ${plural(s.scenes, 'scene')} and ${plural(s.words, 'word')}${entries}${sources}${labels}.`;
}

export const sourceNames: Record<string, string> = {
  markdown: 'a Markdown folder',
  novelcrafter: 'a Novelcrafter export',
  docx: 'a Word document',
  pandoc: 'a pandoc book folder',
  codex_zip: 'a Codex backup',
};
