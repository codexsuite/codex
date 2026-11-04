import { describe, expect, it } from 'vitest';
import { accessOf, readOnlyNote } from './access';

describe('access', () => {
  it('accessOf_WhenTheRoleIsNotKnownYet_ShouldAllowNothing', () => {
    expect(accessOf(undefined)).toMatchObject({ known: false, canEdit: false, isOwner: false });
  });

  it.each([
    ['view', false, false],
    ['comment', false, false],
    ['write', true, false],
    ['owner', true, true],
  ])('accessOf_%s_ShouldSetCanEditAndIsOwner', (role, canEdit, isOwner) => {
    expect(accessOf(role)).toMatchObject({ known: true, canEdit, isOwner });
  });

  it('readOnlyNote_ShouldOnlyAppearForViewersAndCommenters', () => {
    expect(readOnlyNote(accessOf('view'), 'Ana', 'Hollow')).toMatch(
      /^View only\. Ana shared Hollow/,
    );
    expect(readOnlyNote(accessOf('comment'), 'Ana', 'Hollow')).toMatch(/^Comment only/);
    expect(readOnlyNote(accessOf('write'), 'Ana', 'Hollow')).toBeNull();
    expect(readOnlyNote(accessOf(undefined), 'Ana', 'Hollow')).toBeNull();
  });
});
