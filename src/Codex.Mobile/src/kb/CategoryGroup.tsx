import { StyleSheet, Text, Pressable, View } from 'react-native';
import { useStyles } from '~/theme/useStyles';
import type { Theme } from '~/theme';
import type { KbListGroup } from './kbList';
import { CategorySquare } from './CategorySquare';
import { EntryRow } from './EntryRow';
import { kbTestIds } from './testIds';

/** A category heading (square, name, count, `+` for a new entry in it) and its entries. */
export function CategoryGroup({
  bookId,
  group,
  onAdd,
}: {
  bookId: string;
  group: KbListGroup;
  /** Absent for a reader: no `+` on the heading. */
  onAdd?: (categoryId: string) => void;
}) {
  const { styles } = useStyles(makeStyles);
  return (
    <View testID={kbTestIds.group(group.id)}>
      <View style={styles.heading}>
        <CategorySquare colour={group.colour} />
        <Text accessibilityRole="header" style={styles.name}>
          {group.name}
        </Text>
        <Text style={styles.count}>· {group.rows.length}</Text>
        <View style={styles.spacer} />
        {onAdd !== undefined && (
          <Pressable
            testID={kbTestIds.groupAdd(group.id)}
            onPress={() => onAdd(group.id)}
            hitSlop={8}
            accessibilityRole="button"
            accessibilityLabel={`New ${group.name} entry`}
          >
            <Text style={styles.plus}>+</Text>
          </Pressable>
        )}
      </View>
      {group.rows.map((row) => (
        <EntryRow key={row.id} bookId={bookId} row={row} colour={group.colour} />
      ))}
    </View>
  );
}

function makeStyles(theme: Theme) {
  return StyleSheet.create({
    heading: {
      flexDirection: 'row',
      alignItems: 'center',
      gap: theme.space[2],
      paddingHorizontal: theme.space[4],
      paddingTop: theme.space[4],
      paddingBottom: theme.space[1],
    },
    name: {
      fontSize: theme.text['2xs'],
      fontWeight: theme.weight.semibold,
      letterSpacing: 0.6,
      textTransform: 'uppercase',
      color: theme.colors.muted,
    },
    count: { fontSize: theme.text.xs, color: theme.colors.muted },
    spacer: { flex: 1 },
    plus: { fontSize: theme.text.lg, color: theme.colors.accent },
  });
}
