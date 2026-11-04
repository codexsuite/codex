import { StyleSheet, Text, View } from 'react-native';
import { useStyles } from '~/theme/useStyles';
import type { Theme } from '~/theme';
import { kbTestIds } from './testIds';

/** The note above a series book's shared entries. */
export function SeriesNote({ seriesName }: { seriesName: string | null }) {
  const { styles } = useStyles(makeStyles);
  return (
    <View testID={kbTestIds.seriesNote} style={styles.note}>
      <Text style={styles.text}>
        {'From '}
        <Text style={styles.bold}>{seriesName ?? 'the series'}</Text>
        {'. Shared by every book in it; edits change it everywhere.'}
      </Text>
    </View>
  );
}

function makeStyles(theme: Theme) {
  return StyleSheet.create({
    note: {
      margin: theme.space[4],
      marginBottom: 0,
      padding: theme.space[3],
      borderRadius: theme.radius.md,
      backgroundColor: theme.colors['accent-soft'],
    },
    text: { fontSize: theme.text.sm, color: theme.colors.text },
    bold: { fontWeight: theme.weight.semibold },
  });
}
