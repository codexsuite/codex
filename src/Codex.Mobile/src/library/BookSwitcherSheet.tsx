import type { LibraryBook } from '@codex/core/api/types';
import { router, useLocalSearchParams } from 'expo-router';
import { useMemo } from 'react';
import { ActivityIndicator, Pressable, StyleSheet, Text, View } from 'react-native';
import { bookLink, libraryLink } from '~/links';
import { useStyles } from '~/theme/useStyles';
import type { Theme } from '~/theme';
import { Cover, ErrorState, Sheet } from '~/ui';
import { recentBooks, switcherMeta } from './sections';
import { libraryTestIds } from './testIds';
import { useLibrary } from './useLibrary';

const RECENT_LIMIT = 5;

/** The book switcher sheet: recent books (not the open one), then All books in Library. */
export function BookSwitcherSheet() {
  const { theme, styles } = useStyles(makeStyles);
  const { bookId } = useLocalSearchParams<{ bookId: string }>();
  const library = useLibrary();
  const recent = useMemo(
    () => (library.data ? recentBooks(library.data, bookId, RECENT_LIMIT) : []),
    [library.data, bookId],
  );

  function open(book: LibraryBook) {
    // Replace, so Back from the new book reaches the Library and not the book we left.
    router.dismiss();
    router.replace(bookLink(book.id));
  }

  function openLibrary() {
    router.dismiss();
    router.dismissTo(libraryLink());
  }

  return (
    <Sheet title="Switch book">
      <View testID={libraryTestIds.switcherSheet}>
        {library.isPending && (
          <View testID={libraryTestIds.switcherLoading} style={styles.centered}>
            <ActivityIndicator color={theme.colors.accent} />
          </View>
        )}
        {library.isError && (
          <ErrorState
            testID={libraryTestIds.switcherError}
            error={library.error}
            fallback="Couldn't load your books."
            onRetry={() => void library.refetch()}
          />
        )}
        {recent.map((book) => (
          <Pressable
            key={book.id}
            testID={libraryTestIds.switcherBook(book.id)}
            onPress={() => open(book)}
            accessibilityRole="button"
            style={styles.row}
          >
            <Cover palette={book.coverPalette} title={book.title} imageUrl={book.coverUrl} />
            <View style={styles.body}>
              <Text style={styles.title} numberOfLines={1}>
                {book.title}
              </Text>
              <Text style={styles.meta} numberOfLines={1}>
                {switcherMeta(book)}
              </Text>
            </View>
          </Pressable>
        ))}
        <Pressable
          testID={libraryTestIds.switcherAll}
          onPress={openLibrary}
          accessibilityRole="button"
          style={styles.all}
        >
          <Text style={styles.allLabel}>All books in Library</Text>
          <Text style={styles.chevron}>{'›'}</Text>
        </Pressable>
      </View>
    </Sheet>
  );
}

function makeStyles(theme: Theme) {
  return StyleSheet.create({
    centered: { alignItems: 'center', padding: theme.space[5] },
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
    body: { flex: 1, minWidth: 0, gap: 2 },
    title: { fontSize: theme.text.base, fontWeight: theme.weight.medium, color: theme.colors.text },
    meta: { fontSize: theme.text.xs, color: theme.colors.muted },
    all: {
      minHeight: theme.size.row,
      flexDirection: 'row',
      alignItems: 'center',
      justifyContent: 'space-between',
      paddingHorizontal: theme.space[4],
    },
    allLabel: {
      fontSize: theme.text.base,
      fontWeight: theme.weight.medium,
      color: theme.colors.accent,
    },
    chevron: { fontSize: theme.text.lg, color: theme.colors.muted },
  });
}
