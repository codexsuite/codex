import { describe, expect, it } from 'vitest';
import {
  checkArchive,
  checkImages,
  checkSelection,
  describeImport,
  importForm,
  importLimits,
  selectImportArchive,
  selectImportFiles,
  withImages,
} from './importApi';

function fileAt(path: string, bytes: number): File {
  const file = new File([new Uint8Array(bytes)], path.split('/').at(-1) ?? path);
  Object.defineProperty(file, 'webkitRelativePath', { value: path });
  return file;
}

describe('import file selection', () => {
  it('SelectImportFiles_WhenFolderMixed_ShouldSendOnlyMarkdownUnderTheCap', () => {
    const selection = selectImportFiles([
      fileAt('book/index.md', 10),
      fileAt('book/cover.png', 5_000_000),
      fileAt('book/act-01-a/huge.md', importLimits.maxFileBytes + 1),
    ]);

    expect(selection.send.map((f) => f.webkitRelativePath)).toEqual(['book/index.md']);
    expect(selection.notMarkdown).toEqual(['book/cover.png']);
    expect(selection.tooLarge).toEqual(['book/act-01-a/huge.md']);
    expect(selection.bytes).toBe(10);
  });

  it('CheckSelection_WhenMarkdownOverTheTotalCap_ShouldRefuseBeforeUploading', () => {
    const files = Array.from({ length: 13 }, (_, i) =>
      fileAt(`b/n${i}.md`, importLimits.maxFileBytes),
    );

    expect(checkSelection(selectImportFiles(files))).toBe(
      'The Markdown in this folder is 26 MB; an import can be at most 25 MB.',
    );
  });

  it('CheckSelection_WhenImagesAreLarge_ShouldNotCountThem', () => {
    const files = [
      fileAt('b/index.md', 10),
      ...Array.from({ length: 20 }, (_, i) => fileAt(`b/p${i}.png`, 2_000_000)),
    ];

    expect(checkSelection(selectImportFiles(files))).toBeNull();
  });

  it('CheckSelection_WhenTooManyFiles_ShouldRefuse', () => {
    const files = Array.from({ length: importLimits.maxFiles + 1 }, (_, i) =>
      fileAt(`b/n${i}.md`, 1),
    );

    expect(checkSelection(selectImportFiles(files))).toContain('at most 2000 files');
  });

  it('ImportForm_ShouldNameLeftOutFilesWithoutSendingThem', () => {
    const form = importForm(
      'markdown',
      selectImportFiles([fileAt('b/index.md', 1), fileAt('b/c.png', 1)]),
    );

    expect(form.getAll('files')).toHaveLength(1);
    expect(form.getAll('notMarkdown')).toEqual(['b/c.png']);
  });
});

describe('single-file import selection', () => {
  it('SelectImportArchive_WhenZipPicked_ShouldSendItWholeUnderItsName', () => {
    const zip = new File([new Uint8Array(4_000_000)], 'pe.zip');

    const selection = selectImportArchive([zip]);

    expect(selection.send).toEqual([zip]);
    expect(selection.bytes).toBe(4_000_000);
    expect(checkArchive(selection)).toBeNull();
    expect((importForm('novelcrafter', selection).get('files') as File).name).toBe('pe.zip');
  });

  it('CheckArchive_WhenOverTheTotalCap_ShouldRefuseBeforeUploading', () => {
    const big = new File([new Uint8Array(importLimits.maxTotalBytes + 1)], 'big.docx');

    expect(checkArchive(selectImportArchive([big]))).toBe(
      'This file is 26 MB; an import can be at most 25 MB.',
    );
  });

  it('CheckArchive_WhenNothingPicked_ShouldAsk', () => {
    expect(checkArchive(selectImportArchive([]))).toBe('Pick a file to import.');
  });
});

describe('pandoc folder selection', () => {
  // A size the File constructor does not have to fill: only .size is read.
  function bigFileAt(path: string, size: number): File {
    const file = fileAt(path, 1);
    Object.defineProperty(file, 'size', { value: size });
    return file;
  }

  const folder = [
    fileAt('PG/PlayersGuide.yaml', 10),
    fileAt('PG/Chapter 1.md', 20),
    fileAt('PG/images/used.png', 30),
    bigFileAt('PG/images/huge.png', importLimits.maxImageBytes + 1),
    fileAt('PG/images/unused.png', 40),
    fileAt('PG/SD/batch.png', 50),
  ];

  it('SelectImportFiles_WhenPandoc_ShouldSendTheYamlAndNameTheImages', () => {
    const selection = selectImportFiles(folder, 'pandoc');

    expect(selection.send.map((f) => f.webkitRelativePath)).toEqual([
      'PG/PlayersGuide.yaml',
      'PG/Chapter 1.md',
    ]);
    expect(selection.notMarkdown).toEqual([
      'PG/images/used.png',
      'PG/images/huge.png',
      'PG/images/unused.png',
      'PG/SD/batch.png',
    ]);
  });

  it('WithImages_WhenThePreviewListedImages_ShouldSendOnlyThoseUnderTheCap', () => {
    const upload = withImages(selectImportFiles(folder, 'pandoc'), folder, [
      'PG/images/used.png',
      'PG/images/huge.png',
    ]);

    const form = importForm('pandoc', upload);

    expect((form.getAll('files') as File[]).map((f) => f.name)).toEqual([
      'PG/PlayersGuide.yaml',
      'PG/Chapter 1.md',
      'PG/images/used.png',
    ]);
    expect(form.getAll('notMarkdown')).toEqual(['PG/images/unused.png', 'PG/SD/batch.png']);
    expect(form.getAll('tooLarge')).toEqual(['PG/images/huge.png']);
    expect(upload.imageBytes).toBe(30);
    expect(checkImages(upload)).toBeNull();
  });

  it('CheckImages_WhenOverTheImageCap_ShouldRefuseBeforeUploading', () => {
    expect(checkImages({ imageBytes: importLimits.maxImageTotalBytes + 1 })).toBe(
      'The images this book uses are 5 GB; an import can upload at most 4 GB of images.',
    );
  });
});

describe('describeImport', () => {
  const base = {
    importId: 'i',
    bookId: 'b',
    source: 'markdown',
    fileName: 'book',
    acts: 2,
    chapters: 3,
    scenes: 4,
    words: 1200,
    entries: 3,
    skipped: [],
  };

  it('DescribeImport_WhenNoSources_ShouldKeepTheShortSentence', () => {
    expect(describeImport(base)).toBe(
      '2 acts, 3 chapters, 4 scenes and 1,200 words, plus 3 Knowledge base entries.',
    );
  });

  it('DescribeImport_WhenABackup_ShouldAddSourcesAndLabels', () => {
    expect(describeImport({ ...base, sources: 12, citedSources: 10, labels: 4 })).toBe(
      '2 acts, 3 chapters, 4 scenes and 1,200 words, plus 3 Knowledge base entries, 12 sources (10 cited) and 4 labels.',
    );
  });
});
