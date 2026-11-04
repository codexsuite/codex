import { describe, expect, it } from 'vitest';
import { defaultSpeechModelId, findSpeechModel, speechModelCatalog } from './speechModelCatalog';

describe('speech model catalog', () => {
  it('ShouldHaveUniqueIdsAndFileNames', () => {
    expect(new Set(speechModelCatalog.map((m) => m.id)).size).toBe(speechModelCatalog.length);
    expect(new Set(speechModelCatalog.map((m) => m.fileName)).size).toBe(speechModelCatalog.length);
  });

  it('ShouldIncludeTheEnglishModelsTheSliceNeeds', () => {
    for (const id of ['tiny.en', 'base.en', 'small.en']) expect(findSpeechModel(id)).toBeDefined();
  });

  it.each(speechModelCatalog)('$id_ShouldPointAtItsGgmlFileOnHuggingFace', (m) => {
    expect(m.url).toBe(
      `https://huggingface.co/ggerganov/whisper.cpp/resolve/main/ggml-${m.id}.bin`,
    );
    expect(m.fileName).toBe(`ggml-${m.id}.bin`);
    expect(m.sizeBytes).toBeGreaterThan(50_000_000);
  });

  it('SizesShouldGrowFromTinyToSmall', () => {
    const size = (id: string) => findSpeechModel(id)?.sizeBytes ?? 0;
    expect(size('tiny.en')).toBeLessThan(size('base.en'));
    expect(size('base.en')).toBeLessThan(size('small.en'));
  });

  it('DefaultModel_ShouldBeInTheCatalog', () => {
    expect(findSpeechModel(defaultSpeechModelId)).toBeDefined();
  });
});
