import type { OutlineScene } from '@codex/core/api/structure';
import { useState } from 'react';
import { StyleSheet, View } from 'react-native';
import { useKb } from '~/book/queries';
import { useStyles } from '~/theme/useStyles';
import type { Theme } from '~/theme';
import { Chip, EmptyState, Row, TextField } from '~/ui';
import { offeredEntries } from './entryFilter';
import { FieldLabel } from './FieldLabel';
import { MutationError } from './MutationError';
import { planTestIds } from './testIds';
import { useSceneEntries } from './useSceneEntries';

const SHOWN = 8;

/**
 * The scene's Knowledge base entries as chips (tap to unlink) and `+ Entry`, which searches the book's entries to link.
 * A change saves at once.
 */
export function SceneEntriesField({
  bookId,
  scene,
  editable,
}: {
  bookId: string;
  scene: OutlineScene;
  editable: boolean;
}) {
  const { styles } = useStyles(makeStyles);
  const kb = useKb(bookId);
  const { link, unlink } = useSceneEntries(bookId, scene);
  const [picking, setPicking] = useState(false);
  const [query, setQuery] = useState('');
  const offered = offeredEntries(
    kb.data?.entries ?? [],
    scene.linkedEntries.map((entry) => entry.id),
    query,
  );

  return (
    <View style={styles.field}>
      <FieldLabel>Knowledge base</FieldLabel>
      <View style={styles.chips}>
        {scene.linkedEntries.map((entry) => (
          <Chip
            key={entry.id}
            testID={planTestIds.sheetEntry(entry.id)}
            label={editable ? `${entry.name} ×` : entry.name}
            onPress={editable ? () => unlink.mutate(entry.id) : undefined}
          />
        ))}
        {editable && (
          <Chip
            testID={planTestIds.sheetEntryAdd}
            label="+ Entry"
            selected={picking}
            onPress={() => setPicking((p) => !p)}
          />
        )}
      </View>
      {editable && picking && (
        <View testID={planTestIds.sheetEntryPicker} style={styles.picker}>
          <TextField
            testID={planTestIds.sheetEntrySearch}
            placeholder="Link an entry…"
            value={query}
            onChangeText={setQuery}
            autoCapitalize="none"
            autoCorrect={false}
          />
          {offered.slice(0, SHOWN).map((entry) => (
            <Row
              key={entry.id}
              testID={planTestIds.sheetEntryOption(entry.id)}
              label={entry.name}
              onPress={() => {
                link.mutate({ id: entry.id, name: entry.name });
                setQuery('');
              }}
            />
          ))}
          {offered.length === 0 && (
            <EmptyState testID={planTestIds.sheetEntryNone} title="No matching entry" />
          )}
        </View>
      )}
      <MutationError
        error={link.error ?? unlink.error}
        fallback="Couldn't save the entries."
        testID={`${planTestIds.sheetError}-entries`}
      />
    </View>
  );
}

function makeStyles(theme: Theme) {
  return StyleSheet.create({
    field: { gap: theme.space[2] },
    chips: { flexDirection: 'row', flexWrap: 'wrap', gap: theme.space[2] },
    picker: {
      gap: theme.space[1],
      padding: theme.space[3],
      borderWidth: 1,
      borderColor: theme.colors.border,
      borderRadius: theme.radius.md,
      backgroundColor: theme.colors.bg,
    },
  });
}
