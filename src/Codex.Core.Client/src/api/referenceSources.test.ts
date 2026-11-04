import { describe, expect, it } from 'vitest';
import sourcesCsl from '../../../Codex.TestSupport/Fixtures/references/sources-csl.json';
import archiveResult from '../../../Codex.TestSupport/Fixtures/references/archive-result.json';
import recheckResult from '../../../Codex.TestSupport/Fixtures/references/recheck-result.json';
import sourceDetail from '../../../Codex.TestSupport/Fixtures/references/source-detail.json';
import sources from '../../../Codex.TestSupport/Fixtures/references/sources.json';
import {
  sourcesExportUrl,
  type ArchiveSourceResult,
  type RecheckResult,
  type SourceCsl,
  type SourceDetail,
  type SourceRow,
} from './referenceSources';

// The same JSON files the C# SourceDtoFixtureTests round-trip. The annotation makes tsc fail if a type drifts.
const roundTrip = <T>(value: T): T => JSON.parse(JSON.stringify(value)) as T;

describe('reference source contract fixtures', () => {
  it('SourceRowDto list round-trips', () => {
    const typed: SourceRow[] = sources;
    expect(roundTrip(typed)).toEqual(sources);
  });

  it('SourceCslDto list round-trips', () => {
    const typed: SourceCsl[] = sourcesCsl;
    expect(roundTrip(typed)).toEqual(sourcesCsl);
  });

  it('SourceDetailDto round-trips', () => {
    const typed: SourceDetail = sourceDetail as SourceDetail;
    expect(roundTrip(typed)).toEqual(sourceDetail);
  });

  it('RecheckResultDto round-trips', () => {
    const typed: RecheckResult = recheckResult as RecheckResult;
    expect(roundTrip(typed)).toEqual(recheckResult);
  });

  it('ArchiveSourceResult round-trips', () => {
    const typed: ArchiveSourceResult = archiveResult;
    expect(roundTrip(typed)).toEqual(archiveResult);
  });

  it('sourcesExportUrl names the book and the format', () => {
    expect(sourcesExportUrl('b1', 'ris')).toBe('/api/books/b1/sources/export?format=ris');
  });
});
