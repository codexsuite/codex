import { describe, expect, it } from 'vitest';
import apiErrors from '../../../Codex.TestSupport/Fixtures/api-errors.json';
import bookEvents from '../../../Codex.TestSupport/Fixtures/book-events.json';
import bookHeader from '../../../Codex.TestSupport/Fixtures/book-header.json';
import currentUser from '../../../Codex.TestSupport/Fixtures/current-user.json';
import library from '../../../Codex.TestSupport/Fixtures/library.json';
import { apiErrorCodes, isApiError } from './apiError';
import type { Me } from './auth';
import type { BookEvent } from './events';
import type { BookHeader, Library } from './types';

// The same JSON files the C# DtoFixtureTests round-trip. `satisfies` makes tsc fail if a type drifts from them.
const roundTrip = <T>(value: T): T => JSON.parse(JSON.stringify(value)) as T;

describe('shared contract fixtures', () => {
  it('LibraryDto round-trips', () => {
    const typed = library satisfies Library;
    expect(roundTrip(typed)).toEqual(library);
  });

  it('BookHeaderDto round-trips', () => {
    const typed = bookHeader satisfies BookHeader;
    expect(roundTrip(typed)).toEqual(bookHeader);
  });

  it('MeDto round-trips', () => {
    const typed = currentUser satisfies Me;
    expect(roundTrip(typed)).toEqual(currentUser);
  });

  it('book events round-trip', () => {
    const typed: BookEvent[] = bookEvents as BookEvent[];
    expect(roundTrip(typed)).toEqual(bookEvents);
  });

  it('ApiError fixtures pass the guard and cover every code', () => {
    expect(apiErrors.every(isApiError)).toBe(true);
    expect(apiErrors.map((e) => e.code).sort()).toEqual([...apiErrorCodes].sort());
  });

  it('isApiError rejects other shapes', () => {
    expect(isApiError({ error: 'x' })).toBe(false);
    expect(isApiError({ error: 'x', code: 'nope' })).toBe(false);
    expect(isApiError(null)).toBe(false);
  });
});
