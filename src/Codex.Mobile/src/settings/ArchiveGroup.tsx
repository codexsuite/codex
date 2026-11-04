import { router } from 'expo-router';
import { useState } from 'react';
import { libraryLink } from '~/links';
import { Row, SectionHeader } from '~/ui';
import { ArchiveSheet } from './ArchiveSheet';
import { RestoreSheet } from './RestoreSheet';
import type { GroupProps } from './BookGroup';
import { settingsTestIds } from './testIds';

/**
 * Duplicate and Export source stay on desktop (a copy needs a title dialog and the export is a file to download).
 * Archive is the owner's, confirmed first, and returns to Library; an archived book offers Restore instead.
 */
export function ArchiveGroup({
  settings,
  access,
  ownerName,
  save,
}: GroupProps & { ownerName: string }) {
  const [confirming, setConfirming] = useState<'archive' | 'restore' | null>(null);
  const ownerOnly =
    access.isOwner || !access.known ? undefined : `Only ${ownerName}, the owner, can do this`;
  const row = settingsTestIds.row;
  return (
    <>
      <SectionHeader title="Archive & export" />
      <Row testID={row('duplicate')} label="Duplicate book" chip="Desktop" disabled />
      <Row
        testID={row('export-source')}
        label="Export source"
        value=".zip"
        detail="Markdown and Knowledge base, as a file to download"
        chip="Desktop"
        disabled
      />
      {settings.archived ? (
        <Row
          testID={row('restore')}
          label="Restore book"
          detail={ownerOnly ?? 'Archived: hidden from Library. Nothing was deleted.'}
          onPress={access.isOwner ? () => setConfirming('restore') : undefined}
        />
      ) : (
        <Row
          testID={row('archive')}
          label="Archive book"
          danger
          detail={ownerOnly ?? 'Takes it off the shelf; restore from Library'}
          onPress={access.isOwner ? () => setConfirming('archive') : undefined}
        />
      )}
      {confirming === 'restore' && (
        <RestoreSheet
          title={settings.title}
          onConfirm={save.restore}
          onClose={() => setConfirming(null)}
        />
      )}
      {confirming === 'archive' && (
        <ArchiveSheet
          title={settings.title}
          seriesName={settings.seriesName}
          onConfirm={async () => {
            await save.archive();
            router.dismissTo(libraryLink());
          }}
          onClose={() => setConfirming(null)}
        />
      )}
    </>
  );
}
