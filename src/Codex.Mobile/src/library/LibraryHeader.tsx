import { StyleSheet, Text, TextInput, View } from 'react-native';
import { useStyles } from '~/theme/useStyles';
import type { Theme } from '~/theme';
import { libraryTestIds } from './testIds';

/** The 28px large title and the search field under it. */
export function LibraryHeader({
  query,
  onQueryChange,
}: {
  query: string;
  onQueryChange: (query: string) => void;
}) {
  const { theme, styles } = useStyles(makeStyles);
  return (
    <View style={styles.header}>
      <Text testID={libraryTestIds.title} style={styles.title} accessibilityRole="header">
        Library
      </Text>
      <TextInput
        testID={libraryTestIds.search}
        value={query}
        onChangeText={onQueryChange}
        placeholder="Search books and series"
        placeholderTextColor={theme.colors.muted}
        accessibilityLabel="Search books and series"
        autoCapitalize="none"
        autoCorrect={false}
        returnKeyType="search"
        clearButtonMode="while-editing"
        style={styles.search}
      />
    </View>
  );
}

function makeStyles(theme: Theme) {
  return StyleSheet.create({
    header: {
      backgroundColor: theme.colors.surface,
      borderBottomWidth: 1,
      borderBottomColor: theme.colors.border,
      paddingHorizontal: theme.space[3],
      paddingBottom: theme.space[3],
    },
    title: {
      paddingHorizontal: theme.space[1],
      paddingTop: theme.space[3],
      paddingBottom: theme.space[2],
      fontSize: 28,
      fontWeight: theme.weight.semibold,
      letterSpacing: -0.5,
      color: theme.colors.text,
    },
    search: {
      height: theme.size.control,
      borderRadius: theme.size.control / 2,
      paddingHorizontal: theme.space[3],
      fontSize: theme.text.base,
      color: theme.colors.text,
      backgroundColor: theme.colors['surface-2'],
    },
  });
}
