import { useState } from 'react';
import { padded } from '@codex/core/features/settings/seriesNumbers';
import { Row, SectionHeader } from '~/ui';
import { ChoiceSheet } from './ChoiceSheet';
import type { GroupProps } from './BookGroup';
import { settingsTestIds } from './testIds';

/** The series (moving a book is a desktop step) and the book's number, which the owner can change. */
export function SeriesGroup({ settings, access, save }: GroupProps) {
  const [editing, setEditing] = useState(false);
  const numbered = settings.seriesId !== null && settings.seriesNumber !== null;
  const numberEditable = access.isOwner && settings.seriesOrder.length > 0;
  return (
    <>
      <SectionHeader title="Series" />
      <Row
        testID={settingsTestIds.row('series')}
        label="Series"
        value={settings.seriesName ?? 'None (standalone)'}
        chip="Desktop"
        disabled
      />
      {numbered && settings.seriesNumber !== null && (
        <Row
          testID={settingsTestIds.row('number')}
          label="Number"
          value={`Book ${padded(settings.seriesNumber)}`}
          detail="Shares the series Knowledge base, labels, style guide and publication settings"
          onPress={numberEditable ? () => setEditing(true) : undefined}
        />
      )}
      {editing && settings.seriesNumber !== null && (
        <ChoiceSheet
          title="Number in the series"
          choices={settings.seriesOrder.map((_, index) => ({
            value: String(index + 1),
            label: padded(index + 1),
          }))}
          selected={String(settings.seriesNumber)}
          note="The other books renumber to make room."
          onSave={(value) =>
            Number(value) === settings.seriesNumber
              ? Promise.resolve()
              : save.setSeriesNumber(Number(value))
          }
          onClose={() => setEditing(false)}
        />
      )}
    </>
  );
}
