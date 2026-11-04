import { describe, expect, it } from 'vitest';
import sharing from '../../../Codex.TestSupport/Fixtures/sharing/sharing.json';
import readOnly from '../../../Codex.TestSupport/Fixtures/sharing/sharing-read-only.json';
import lookupRequest from '../../../Codex.TestSupport/Fixtures/sharing/sharing-lookup-request.json';
import user from '../../../Codex.TestSupport/Fixtures/sharing/sharing-user.json';
import setGrantRequest from '../../../Codex.TestSupport/Fixtures/sharing/set-grant-request.json';
import grant from '../../../Codex.TestSupport/Fixtures/sharing/sharing-grant.json';
import { bookRoles, hasRole, parseBookRole } from './bookRole';
import type {
  SetGrantRequest,
  Sharing,
  SharingGrant,
  SharingLookupRequest,
  SharingUser,
} from './types';

// The same JSON files the C# SharingDtoFixtureTests round-trip. `satisfies` makes tsc fail if a type drifts.
const roundTrip = <T>(value: T): T => JSON.parse(JSON.stringify(value)) as T;

describe('sharing contract fixtures', () => {
  it('SharingDto round-trips', () => {
    expect(roundTrip(sharing satisfies Sharing)).toEqual(sharing);
    expect(roundTrip(readOnly satisfies Sharing)).toEqual(readOnly);
  });
  it('SharingLookupRequest round-trips', () => {
    expect(roundTrip(lookupRequest satisfies SharingLookupRequest)).toEqual(lookupRequest);
  });
  it('SharingUserDto round-trips', () => {
    expect(roundTrip(user satisfies SharingUser)).toEqual(user);
  });
  it('SetGrantRequest round-trips', () => {
    expect(roundTrip(setGrantRequest satisfies SetGrantRequest)).toEqual(setGrantRequest);
  });
  it('SharingGrantDto round-trips', () => {
    expect(roundTrip(grant satisfies SharingGrant)).toEqual(grant);
  });
});

describe('parseBookRole', () => {
  it('accepts the four wire values and orders them', () => {
    expect(bookRoles.map(parseBookRole)).toEqual(['view', 'comment', 'write', 'owner']);
    expect(hasRole('write', 'comment')).toBe(true);
    expect(hasRole('view', 'write')).toBe(false);
  });
  it('rejects anything else', () => {
    expect(() => parseBookRole('admin')).toThrow();
  });
});
