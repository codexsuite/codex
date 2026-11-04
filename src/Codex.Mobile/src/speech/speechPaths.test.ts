import { describe, expect, it } from 'vitest';
import { findSpeechModel, speechModelCatalog } from './speechModelCatalog';
import { installedFrom, languageFor, partialFileName, progressTotal } from './speechPaths';

const base = findSpeechModel('base.en')!;
const multilingual = findSpeechModel('base')!;

describe('installedFrom', () => {
  it('WithAFileOfTheCatalogSize_ShouldListTheModel', () => {
    const files = [{ name: base.fileName, sizeBytes: base.sizeBytes }];
    expect(installedFrom(files, speechModelCatalog)).toEqual([
      { id: 'base.en', sizeBytes: base.sizeBytes },
    ]);
  });

  it('WithAShortFile_ShouldNotListIt', () => {
    expect(installedFrom([{ name: base.fileName, sizeBytes: 1000 }], speechModelCatalog)).toEqual(
      [],
    );
  });

  it('WithAPartialFileOrAStranger_ShouldIgnoreThem', () => {
    const files = [
      { name: partialFileName(base), sizeBytes: base.sizeBytes },
      { name: 'notes.txt', sizeBytes: 3 },
    ];
    expect(installedFrom(files, speechModelCatalog)).toEqual([]);
  });
});

describe('partialFileName', () => {
  it('ShouldNotBeTheFinalName', () => {
    expect(partialFileName(base)).toBe('ggml-base.en.bin.part');
  });
});

describe('languageFor', () => {
  it('EnglishModel_ShouldBeEn', () => expect(languageFor(base)).toBe('en'));
  it('MultilingualModel_ShouldDetect', () => expect(languageFor(multilingual)).toBe('auto'));
});

describe('progressTotal', () => {
  it('WhenTheServerSentNoLength_ShouldUseTheCatalogSize', () => {
    expect(progressTotal(-1, base)).toBe(base.sizeBytes);
  });
  it('WhenTheServerSentOne_ShouldUseIt', () => {
    expect(progressTotal(123, base)).toBe(123);
  });
});
