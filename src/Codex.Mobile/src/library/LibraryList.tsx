import type { Library, LibraryBook } from '@codex/core/api/types';
import { useCallback, useMemo, useState } from 'react';
import { RefreshControl, SectionList, StyleSheet, Text } from 'react-native';
import { useSafeAreaInsets } from 'react-native-safe-area-context';
import { useStyles } from '~/theme/useStyles';
import type { Theme } from '~/theme';
import { EmptyState, SectionHeader } from '~/ui';
import { BookRow } from './BookRow';
import { librarySections } from './sections';
import { libraryTestIds } from './testIds';

/** The series groups and Standalone, with pull to refresh. `header` (the Continue card) scrolls with the list. */
export function LibraryList({
  library,
  searching,
  header,
  onOpenBook,
  onRefresh,
}: {
  library: Library;
  /** True while the search field holds text: an empty list then means no matches, not an empty library. */
  searching: boolean;
  header: React.ReactElement | null;
  onOpenBook: (book: LibraryBook) => void;
  onRefresh: () => Promise<unknown>;
}) {
  const { theme, styles } = useStyles(makeStyles);
  const insets = useSafeAreaInsets();
  const [refreshing, setRefreshing] = useState(false);
  const sections = useMemo(() => librarySections(library), [library]);

  const refresh = useCallback(async () => {
    setRefreshing(true);
    try {
      await onRefresh();
    } finally {
      setRefreshing(false);
    }
  }, [onRefresh]);

  return (
    <SectionList
      testID={libraryTestIds.list}
      sections={sections}
      keyExtractor={(book) => book.id}
      keyboardShouldPersistTaps="handled"
      keyboardDismissMode="on-drag"
      stickySectionHeadersEnabled={false}
      ListHeaderComponent={header ?? undefined}
      renderSectionHeader={({ section }) => (
        <SectionHeader
          title={section.title}
          count={section.bookCount}
          testID={libraryTestIds.sectionHeader(section.key)}
        />
      )}
      renderItem={({ item }) => <BookRow book={item} onPress={() => onOpenBook(item)} />}
      ListFooterComponent={
        <Text testID={libraryTestIds.footer} style={styles.footer}>
          Import and series settings are on desktop.
        </Text>
      }
      ListEmptyComponent={
        searching ? (
          <EmptyState
            testID={libraryTestIds.noMatches}
            title="No matches"
            message="No book or series matches your search."
          />
        ) : (
          <EmptyState
            testID={libraryTestIds.empty}
            title="No books yet"
            message="Create your first book on desktop. It shows up here."
          />
        )
      }
      refreshControl={
        <RefreshControl
          refreshing={refreshing}
          onRefresh={() => void refresh()}
          tintColor={theme.colors.accent}
        />
      }
      contentContainerStyle={{ paddingBottom: insets.bottom + theme.space[4] }}
      style={styles.list}
    />
  );
}

function makeStyles(theme: Theme) {
  return StyleSheet.create({
    list: { flex: 1, backgroundColor: theme.colors.bg },
    footer: {
      padding: theme.space[4],
      textAlign: 'center',
      fontSize: theme.text.xs,
      color: theme.colors.muted,
    },
  });
}
