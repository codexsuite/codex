import { lastScreenLink, lastScreenMemory } from '~/book/lastScreen';
import { accountLink, bookLink } from '~/links';
import { defaultLibraryParams, libraryKey } from '@codex/core/api/library';
import { useQueryClient } from '@tanstack/react-query';
import { useFocusEffect, router } from 'expo-router';
import { useCallback, useMemo, useState } from 'react';
import { ActivityIndicator, StyleSheet, View } from 'react-native';
import { useSafeAreaInsets } from 'react-native-safe-area-context';
import { useStyles } from '~/theme/useStyles';
import type { Theme } from '~/theme';
import { ErrorState, SectionHeader } from '~/ui';
import { ContinueCard } from './ContinueCard';
import { LibraryHeader } from './LibraryHeader';
import { LibraryList } from './LibraryList';
import { RefreshFailedNotice } from './RefreshFailedNotice';
import { LibraryTopBar } from './LibraryTopBar';
import { filterLibrary, findBook, tabLabel } from './sections';
import { libraryTestIds } from './testIds';
import { useLibrary } from './useLibrary';
import { useMe } from './useMe';

/** The Library: avatar, large title, search, Continue card, then books grouped by series and Standalone. */
export function LibraryScreen() {
  const { theme, styles } = useStyles(makeStyles);
  const insets = useSafeAreaInsets();
  const library = useLibrary();
  const queryClient = useQueryClient();
  const me = useMe();
  const [query, setQuery] = useState('');
  const [last, setLast] = useState(readLast);

  // Library stays mounted under a book. Whenever it is shown again, read where you were (the book layout records it)
  // and reload the books if they have gone stale (word counts and edited labels change while you write).
  useFocusEffect(
    useCallback(() => {
      setLast(readLast());
      const cached = queryClient
        .getQueryCache()
        .find({ queryKey: libraryKey(defaultLibraryParams) });
      if (cached?.state.fetchStatus === 'idle' && cached.isStale()) void cached.fetch();
    }, [queryClient]),
  );

  const { data, refetch } = library;
  const shown = useMemo(() => (data ? filterLibrary(data, query) : null), [data, query]);
  const continueBook = data && last ? findBook(data, last.bookId) : null;

  const refresh = useCallback(() => refetch(), [refetch]);

  return (
    <View testID={libraryTestIds.screen} style={styles.screen}>
      <View style={{ height: insets.top, backgroundColor: theme.colors.surface }} />
      <LibraryTopBar
        initials={me.data?.initials ?? ''}
        onAccount={() => router.push(accountLink())}
      />
      <LibraryHeader query={query} onQueryChange={setQuery} />
      {!data && library.isPending ? (
        <View testID={libraryTestIds.loading} style={styles.centered}>
          <ActivityIndicator color={theme.colors.accent} />
        </View>
      ) : !data && library.isError ? (
        <ErrorState
          testID={libraryTestIds.error}
          error={library.error}
          fallback="Couldn't load your library."
          onRetry={() => void library.refetch()}
        />
      ) : (
        shown && (
          <>
            {library.isError && <RefreshFailedNotice />}
            <LibraryList
              library={shown}
              searching={query.trim() !== ''}
              header={
                continueBook && last && query.trim() === '' ? (
                  <>
                    <SectionHeader title="Continue" testID={libraryTestIds.continueHeading} />
                    <ContinueCard
                      book={continueBook}
                      where={tabLabel(last.screen.tab)}
                      onPress={() => router.push(lastScreenLink(continueBook.id, last.screen))}
                    />
                  </>
                ) : null
              }
              onOpenBook={(book) => router.push(bookLink(book.id))}
              onRefresh={refresh}
            />
          </>
        )
      )}
    </View>
  );
}

function readLast() {
  const bookId = lastScreenMemory.lastBookId();
  if (bookId === null) return null;
  return {
    bookId,
    screen: lastScreenMemory.screenOf(bookId) ?? { tab: 'plan' as const, pushed: null },
  };
}

function makeStyles(theme: Theme) {
  return StyleSheet.create({
    screen: { flex: 1, backgroundColor: theme.colors.bg },
    centered: { flex: 1, alignItems: 'center', justifyContent: 'center', padding: theme.space[5] },
  });
}
