export const importSources = [
  {
    id: 'markdown',
    name: 'Markdown folder',
    sub: 'Acts, chapters and scenes as folders and files (act-01-…/chapter-01-…/scene-01.md).',
    drop: 'Drop a folder of .md files',
    folder: true,
    accept: undefined,
  },
  {
    id: 'pandoc',
    name: 'Pandoc book folder (Markdown + LaTeX)',
    sub: 'Chapter files in the order of a pandoc .yaml (or Chapter 1.md, Chapter 2.md…), with dndbook LaTeX. Only the images the chapters use are uploaded.',
    drop: 'Drop the book folder',
    folder: true,
    accept: undefined,
  },
  {
    id: 'novelcrafter',
    name: 'Novelcrafter export',
    sub: 'The .zip from Novelcrafter: acts, chapters, scenes and the Codex.',
    drop: 'Drop a Novelcrafter .zip export',
    folder: false,
    accept: '.zip',
  },
  {
    id: 'docx',
    name: 'Word document (.docx)',
    sub: 'Heading 1 starts a chapter and Heading 2 a scene. Section breaks split scenes.',
    drop: 'Drop a .docx file',
    folder: false,
    accept: '.docx',
  },
  {
    id: 'codex_zip',
    name: 'Codex backup (.zip)',
    sub: 'A backup made by Export source in Book settings: the whole book, with its Knowledge base, References and labels.',
    drop: 'Drop a Codex backup .zip',
    folder: false,
    accept: '.zip',
  },
] as const;

export type ImportSource = (typeof importSources)[number];
export type ImportSourceId = ImportSource['id'];

/** "N files" for a folder, the file's name and size otherwise. */
export function describePicked(files: readonly File[], source: ImportSource): string {
  if (source.folder) return `${files.length} file${files.length === 1 ? '' : 's'}`;
  const [file] = files;
  return file === undefined ? '' : `${file.name} · ${Math.max(1, Math.round(file.size / 1024))} KB`;
}
