import { describe, expect, it } from 'vitest';
import bookSettings from '../../../Codex.TestSupport/Fixtures/settings/book-settings.json';
import patchRequest from '../../../Codex.TestSupport/Fixtures/settings/patch-book-settings-request.json';
import setNumberRequest from '../../../Codex.TestSupport/Fixtures/settings/set-series-number-request.json';
import numberChange from '../../../Codex.TestSupport/Fixtures/settings/series-number-change.json';
import moveRequest from '../../../Codex.TestSupport/Fixtures/settings/series-move-request.json';
import movePreview from '../../../Codex.TestSupport/Fixtures/settings/series-move-preview.json';
import publication from '../../../Codex.TestSupport/Fixtures/settings/publication-settings.json';
import patchPublication from '../../../Codex.TestSupport/Fixtures/settings/patch-publication-settings-request.json';
import rulesImpact from '../../../Codex.TestSupport/Fixtures/settings/rules-impact.json';
import layout from '../../../Codex.TestSupport/Fixtures/settings/layout-settings.json';
import duplicated from '../../../Codex.TestSupport/Fixtures/settings/duplicated-book.json';
import type {
  BookSettings,
  DuplicatedBook,
  LayoutSettings,
  PatchPublicationSettingsRequest,
  PublicationSettings,
  RulesImpact,
  PatchBookSettingsRequest,
  SeriesMovePreview,
  SeriesMoveRequest,
  SeriesNumberChange,
  SetSeriesNumberRequest,
} from './settings';

// The same JSON files the C# SettingsDtoFixtureTests round-trip. `satisfies` makes tsc fail if a type drifts.
const roundTrip = <T>(value: T): T => JSON.parse(JSON.stringify(value)) as T;

describe('settings contract fixtures', () => {
  it('BookSettingsDto round-trips', () => {
    expect(roundTrip(bookSettings satisfies BookSettings)).toEqual(bookSettings);
  });
  it('PatchBookSettingsRequest round-trips', () => {
    expect(roundTrip(patchRequest satisfies PatchBookSettingsRequest)).toEqual(patchRequest);
  });
  it('SetSeriesNumberRequest round-trips', () => {
    expect(roundTrip(setNumberRequest satisfies SetSeriesNumberRequest)).toEqual(setNumberRequest);
  });
  it('SeriesNumberChangeDto round-trips', () => {
    expect(roundTrip(numberChange satisfies SeriesNumberChange)).toEqual(numberChange);
  });
  it('SeriesMoveRequest round-trips', () => {
    expect(roundTrip(moveRequest satisfies SeriesMoveRequest)).toEqual(moveRequest);
  });
  it('SeriesMovePreviewDto round-trips', () => {
    expect(roundTrip(movePreview satisfies SeriesMovePreview)).toEqual(movePreview);
  });
  it('PublicationSettingsDto round-trips', () => {
    expect(roundTrip(publication as PublicationSettings)).toEqual(publication);
  });
  it('PatchPublicationSettingsRequest round-trips', () => {
    expect(roundTrip(patchPublication as PatchPublicationSettingsRequest)).toEqual(
      patchPublication,
    );
  });
  it('RulesImpactDto round-trips', () => {
    expect(roundTrip(rulesImpact satisfies RulesImpact)).toEqual(rulesImpact);
  });
  it('LayoutSettingsDto round-trips', () => {
    expect(roundTrip(layout satisfies LayoutSettings)).toEqual(layout);
  });
  it('DuplicatedBookDto round-trips', () => {
    expect(roundTrip(duplicated satisfies DuplicatedBook)).toEqual(duplicated);
  });
});
