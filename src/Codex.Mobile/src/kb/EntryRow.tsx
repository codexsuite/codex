import { router } from 'expo-router';
import { Pressable, StyleSheet, Text, View } from 'react-native';
import { entryLink } from '~/links';
import { useStyles } from '~/theme/useStyles';
import type { Theme } from '~/theme';
import type { KbListRow } from './kbList';
import { CategorySquare } from './CategorySquare';
import { kbTestIds } from './testIds';

/** One entry of the list: category square, name, `Always` tag, one line under it, scene count. Opens the entry. */
export function EntryRow({
  bookId,
  row,
  colour,
}: {
  bookId: string;
  row: KbListRow;
  colour: string | null;
}) {
  const { styles } = useStyles(makeStyles);
  return (
    <Pressable
      testID={kbTestIds.row(row.id)}
      onPress={() => router.push(entryLink(bookId, row.id))}
      accessibilityRole="button"
      style={styles.row}
    >
      <CategorySquare colour={colour} />
      <View style={styles.body}>
        <View style={styles.title}>
          <Text style={styles.name} numberOfLines={1}>
            {row.name}
          </Text>
          {row.alwaysInContext && <Text style={styles.always}>Always</Text>}
        </View>
        {row.summaryLine !== '' && (
          <Text style={styles.line} numberOfLines={1}>
            {row.summaryLine}
          </Text>
        )}
      </View>
      <Text style={styles.count} accessibilityLabel={`${row.sceneCount} scenes`}>
        {row.sceneCount}
      </Text>
    </Pressable>
  );
}

function makeStyles(theme: Theme) {
  return StyleSheet.create({
    row: {
      minHeight: theme.size.row,
      flexDirection: 'row',
      alignItems: 'center',
      gap: theme.space[3],
      paddingVertical: theme.space[2],
      paddingHorizontal: theme.space[4],
      backgroundColor: theme.colors.surface,
      borderBottomWidth: StyleSheet.hairlineWidth,
      borderBottomColor: theme.colors.border,
    },
    body: { flex: 1, gap: 2 },
    title: { flexDirection: 'row', alignItems: 'center', gap: theme.space[2] },
    name: {
      flexShrink: 1,
      fontSize: theme.text.base,
      fontWeight: theme.weight.medium,
      color: theme.colors.text,
    },
    always: {
      fontSize: theme.text['2xs'],
      fontWeight: theme.weight.semibold,
      color: theme.colors.accent,
      backgroundColor: theme.colors['accent-soft'],
      paddingHorizontal: theme.space[2],
      borderRadius: theme.radius.xs,
      overflow: 'hidden',
    },
    line: { fontSize: theme.text.sm, color: theme.colors.muted },
    count: { fontSize: theme.text.sm, color: theme.colors.muted },
  });
}
