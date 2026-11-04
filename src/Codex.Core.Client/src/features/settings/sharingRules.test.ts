import { describe, expect, it } from 'vitest';
import type { Sharing, SharingUser } from '../../api/types';
import {
  initials,
  isCompleteEmail,
  lookupOutcome,
  nameList,
  otherAccess,
  peopleCount,
  removedToast,
  rolesAbove,
} from './sharingRules';

const user = (id: string, displayName: string): SharingUser => ({ id, displayName, email: null });
const jd = user('jd', 'Joel Dickson');
const ana = user('ana', 'Ana Kowalczyk');
const bo = user('bo', 'Bo Lindqvist');
const cy = user('cy', 'Cy Moreau');

const sharing: Sharing = {
  owner: jd,
  role: 'owner',
  book: [
    { user: ana, role: 'view' },
    { user: cy, role: 'write' },
  ],
  series: {
    id: 'fg',
    title: 'The Forsaken Gods',
    grants: [
      { user: ana, role: 'write' },
      { user: bo, role: 'view' },
      { user: cy, role: 'comment' },
    ],
  },
};

describe('isCompleteEmail', () => {
  it('Email_WhenIncomplete_ShouldNotLookUp', () => {
    for (const text of [
      '',
      'ana',
      'ana@',
      'ana@codex',
      'ana@codex.',
      'ana@codex.c',
      '@codex.local',
    ])
      expect(isCompleteEmail(text)).toBe(false);
  });

  it('Email_WhenWhole_ShouldLookUpWhateverTheSpacesAndCase', () => {
    expect(isCompleteEmail('ana@codex.co')).toBe(true);
    expect(isCompleteEmail('  Ana@Codex.Local ')).toBe(true);
  });
});

describe('sharing rules', () => {
  it('Count_ShouldCountEachPersonOnce', () => {
    expect(peopleCount(sharing)).toBe(3);
  });

  it('OtherAccess_ShouldNoteTheHigherOrEqualSeriesRoleOnTheBookList', () => {
    expect(otherAccess(sharing, 'book', sharing.book[0])).toBe('Can edit through the series');
    expect(otherAccess(sharing, 'book', sharing.book[1])).toBe('');
    expect(otherAccess(sharing, 'series', sharing.series!.grants[2])).toBe('Can edit on this book');
    expect(otherAccess(sharing, 'series', sharing.series!.grants[1])).toBe('');
  });

  it('Lookup_ShouldTellOwnerExistingSeriesEditAndFloor', () => {
    expect(lookupOutcome(sharing, 'book', jd)).toEqual({ kind: 'owner' });
    expect(lookupOutcome(sharing, 'book', ana)).toEqual({ kind: 'has', role: 'view' });
    expect(lookupOutcome(sharing, 'series', bo)).toEqual({ kind: 'has', role: 'view' });
    expect(lookupOutcome(sharing, 'book', bo)).toEqual({ kind: 'match', floor: 'view' });
    const dee = user('dee', 'Dee');
    expect(lookupOutcome(sharing, 'book', dee)).toEqual({ kind: 'match', floor: null });
    const withEditor: Sharing = { ...sharing, book: [] };
    expect(lookupOutcome(withEditor, 'book', ana)).toEqual({ kind: 'series-edit' });
    expect(rolesAbove('view')).toEqual(['comment', 'write']);
    expect(rolesAbove(null)).toEqual(['view', 'comment', 'write']);
  });

  it('Removed_ShouldSayWhatThePersonKeeps', () => {
    expect(removedToast(sharing, 'book', ana)).toBe(
      'Removed Ana Kowalczyk. They keep Can edit through The Forsaken Gods.',
    );
    expect(removedToast(sharing, 'series', cy)).toBe(
      'Removed Cy Moreau from The Forsaken Gods. They keep Can edit on this book.',
    );
    expect(removedToast(sharing, 'series', bo)).toBe(
      'Removed Bo Lindqvist from The Forsaken Gods. Their open tabs go back to Library.',
    );
  });

  it('Names_ShouldJoinAndAbbreviate', () => {
    expect(nameList(['Ana'])).toBe('Ana');
    expect(nameList(['Ana', 'Bo', 'Cy'])).toBe('Ana, Bo and Cy');
    expect(initials('Ana Maria Kowalczyk')).toBe('AK');
    expect(initials('cher')).toBe('CH');
  });
});
