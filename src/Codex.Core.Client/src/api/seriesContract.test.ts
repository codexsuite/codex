import { describe, expect, it } from 'vitest';
import patchSeriesRequest from '../../../Codex.TestSupport/Fixtures/library/patch-series-request.json';
import series from '../../../Codex.TestSupport/Fixtures/library/series.json';
import seriesOrder from '../../../Codex.TestSupport/Fixtures/library/series-order.json';
import setSeriesOrderRequest from '../../../Codex.TestSupport/Fixtures/library/set-series-order-request.json';
import createBookRequest from '../../../Codex.TestSupport/Fixtures/library/create-book-request.json';
import createdBook from '../../../Codex.TestSupport/Fixtures/library/created-book.json';
import nextSeriesNumber from '../../../Codex.TestSupport/Fixtures/library/next-series-number.json';
import type {
  CreateBookRequest,
  CreatedBook,
  NextSeriesNumber,
  PatchSeriesRequest,
  Series,
  SeriesOrder,
  SetSeriesOrderRequest,
} from './library';

// The same JSON files the C# SeriesDtoFixtureTests round-trip. `satisfies` makes tsc fail if a type drifts.
const roundTrip = <T>(value: T): T => JSON.parse(JSON.stringify(value)) as T;

describe('library series contract fixtures', () => {
  it('SeriesDto round-trips', () => {
    expect(roundTrip(series satisfies Series)).toEqual(series);
  });
  it('PatchSeriesRequest round-trips', () => {
    expect(roundTrip(patchSeriesRequest satisfies PatchSeriesRequest)).toEqual(patchSeriesRequest);
  });
  it('SetSeriesOrderRequest round-trips', () => {
    expect(roundTrip(setSeriesOrderRequest satisfies SetSeriesOrderRequest)).toEqual(
      setSeriesOrderRequest,
    );
  });
  it('SeriesOrderDto round-trips', () => {
    expect(roundTrip(seriesOrder satisfies SeriesOrder)).toEqual(seriesOrder);
  });
});

describe('library book contract fixtures', () => {
  it('CreateBookRequest round-trips', () => {
    expect(roundTrip(createBookRequest satisfies CreateBookRequest)).toEqual(createBookRequest);
  });
  it('CreatedBookDto round-trips', () => {
    expect(roundTrip(createdBook satisfies CreatedBook)).toEqual(createdBook);
  });
  it('NextSeriesNumberDto round-trips', () => {
    expect(roundTrip(nextSeriesNumber satisfies NextSeriesNumber)).toEqual(nextSeriesNumber);
  });
});
