import { Pressable, StyleSheet, Text, TextInput, View } from 'react-native';
import { useStyles } from '~/theme/useStyles';
import type { Theme } from '~/theme';
import { kbTestIds } from './testIds';

/** The search field and the `+` that starts a new entry. */
export function SearchBar({
  query,
  total,
  onQuery,
  onAdd,
}: {
  query: string;
  total: number;
  onQuery: (query: string) => void;
  /** Absent for a reader: no `+`. */
  onAdd?: () => void;
}) {
  const { theme, styles } = useStyles(makeStyles);
  return (
    <View style={styles.bar}>
      <TextInput
        testID={kbTestIds.search}
        value={query}
        onChangeText={onQuery}
        placeholder={`Search ${total} ${total === 1 ? 'entry' : 'entries'}`}
        placeholderTextColor={theme.colors.muted}
        autoCapitalize="none"
        autoCorrect={false}
        returnKeyType="search"
        accessibilityLabel="Search entries"
        style={styles.input}
      />
      {onAdd !== undefined && (
        <Pressable
          testID={kbTestIds.add}
          onPress={onAdd}
          accessibilityRole="button"
          accessibilityLabel="New entry"
          style={styles.add}
        >
          <Text style={styles.plus}>+</Text>
        </Pressable>
      )}
    </View>
  );
}

function makeStyles(theme: Theme) {
  return StyleSheet.create({
    bar: {
      flexDirection: 'row',
      alignItems: 'center',
      gap: theme.space[2],
      paddingHorizontal: theme.space[4],
      paddingTop: theme.space[2],
    },
    input: {
      flex: 1,
      height: theme.size.control,
      paddingHorizontal: theme.space[3],
      borderRadius: theme.radius.md,
      backgroundColor: theme.colors['surface-2'],
      fontSize: theme.text.base,
      color: theme.colors.text,
    },
    add: {
      width: theme.size.control,
      height: theme.size.control,
      alignItems: 'center',
      justifyContent: 'center',
      borderRadius: theme.radius.md,
      backgroundColor: theme.colors.accent,
    },
    plus: { fontSize: theme.text.lg, color: theme.colors['accent-fg'] },
  });
}
