import { useState } from 'react';
import type { BookSettings } from '@codex/core/api/settings';
import { Row, SectionHeader } from '~/ui';
import { ChoiceSheet } from './ChoiceSheet';
import type { SettingsAccess } from './access';
import { countText, parseWholeNumber, statusLabel, statusOptions } from './fieldValues';
import { settingsTestIds } from './testIds';
import { TextFieldSheet } from './TextFieldSheet';
import type { useSettingsMutations } from './useSettingsMutations';

type Editing = 'title' | 'author' | 'status' | 'target';

export interface GroupProps {
  settings: BookSettings;
  access: SettingsAccess;
  save: ReturnType<typeof useSettingsMutations>;
}

/** Title, author, status, target words (each a sheet) and the cover (desktop only). */
export function BookGroup({ settings, access, save }: GroupProps) {
  const [editing, setEditing] = useState<Editing | null>(null);
  const close = () => setEditing(null);
  const editable = access.canEdit ? (field: Editing) => () => setEditing(field) : () => undefined;
  // Once published, only the owner changes the status (the desktop disables it for editors).
  const statusEditable = access.isOwner || (access.canEdit && settings.status !== 'published');
  const row = settingsTestIds.row;

  return (
    <>
      <SectionHeader title="Book" />
      <Row testID={row('title')} label="Title" value={settings.title} onPress={editable('title')} />
      <Row
        testID={row('author')}
        label="Author"
        value={settings.authorName ?? 'Not set'}
        onPress={editable('author')}
      />
      <Row
        testID={row('status')}
        label="Status"
        value={statusLabel(settings.status)}
        onPress={statusEditable ? () => setEditing('status') : undefined}
      />
      <Row
        testID={row('target')}
        label="Target words"
        value={countText(settings.targetWords)}
        onPress={editable('target')}
      />
      <Row testID={row('cover')} label="Cover" chip="Desktop" disabled />

      {editing === 'title' && (
        <TextFieldSheet
          title="Title"
          label="Title"
          initial={settings.title}
          validate={(text) => (text.trim() === '' ? 'A book needs a title.' : null)}
          onSave={(text) => save.saveBook({ title: text })}
          onClose={close}
        />
      )}
      {editing === 'author' && (
        <TextFieldSheet
          title="Author"
          label="Author"
          initial={settings.authorName ?? ''}
          hint="As printed on the cover, title page and permission letters. A pen name is fine."
          validate={() => null}
          onSave={(text) => save.saveBook({ authorName: text })}
          onClose={close}
        />
      )}
      {editing === 'target' && (
        <TextFieldSheet
          title="Target words"
          label="Target words"
          initial={settings.targetWords === null ? '' : String(settings.targetWords)}
          keyboardType="number-pad"
          hint="Leave it blank for no target."
          validate={(text) => {
            const parsed = parseWholeNumber(text, { allowBlank: true });
            return parsed.ok ? null : parsed.message;
          }}
          onSave={(text) => {
            const parsed = parseWholeNumber(text, { allowBlank: true });
            return parsed.ok ? save.saveBook({ targetWords: parsed.value }) : Promise.resolve();
          }}
          onClose={close}
        />
      )}
      {editing === 'status' && (
        <ChoiceSheet
          title="Status"
          choices={statusOptions.map((o) => ({
            value: o.value,
            label: o.label,
            disabled: o.value === 'published' && settings.status !== 'published',
          }))}
          selected={settings.status}
          note={
            access.isOwner
              ? 'Marking a book Published takes a snapshot of it; do that on desktop.'
              : 'Only the owner can mark a book Published.'
          }
          onSave={(status) =>
            status === settings.status ? Promise.resolve() : save.saveBook({ status })
          }
          onClose={close}
        />
      )}
    </>
  );
}
