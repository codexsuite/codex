import { useState } from 'react';
import { StyleSheet, View } from 'react-native';
import type { KbEntrySummary } from '@codex/core/api/kb';
import { Row, TextField } from '~/ui';
import type { Theme } from '../theme';
import { useStyles } from '../theme/useStyles';
import { recordTestIds } from './testIds';

/** A searchable list of the book's entries; tapping one chooses it. */
export function EntryPicker({
  entries,
  pickedId,
  onPick,
}: {
  entries: readonly KbEntrySummary[];
  pickedId: string | null;
  onPick: (id: string) => void;
}) {
  const { styles } = useStyles(makeStyles);
  const [query, setQuery] = useState('');
  const shown = entries.filter((entry) =>
    entry.name.toLowerCase().includes(query.trim().toLowerCase()),
  );
  return (
    <View style={styles.box}>
      <TextField label="Entry" value={query} onChangeText={setQuery} placeholder="Search entries" />
      {shown.map((entry) => (
        <Row
          key={entry.id}
          label={entry.name}
          value={entry.id === pickedId ? 'Selected' : undefined}
          onPress={() => onPick(entry.id)}
          testID={recordTestIds.pickEntry(entry.id)}
        />
      ))}
    </View>
  );
}

function makeStyles(theme: Theme) {
  return StyleSheet.create({ box: { gap: theme.space[2] } });
}
