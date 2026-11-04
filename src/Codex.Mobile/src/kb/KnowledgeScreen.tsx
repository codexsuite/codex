import { useQueryClient } from '@tanstack/react-query';
import { useMemo, useState } from 'react';
import { ActivityIndicator, RefreshControl, ScrollView, StyleSheet, View } from 'react-native';
import { kbKey } from '@codex/core/api/kb';
import { useBookId } from '~/book/useBookId';
import { useKb } from '~/book/queries';
import { useStyles } from '~/theme/useStyles';
import type { Theme } from '~/theme';
import { EmptyState, ErrorState } from '~/ui';
import { CategoryGroup } from './CategoryGroup';
import { CategoryStrip } from './CategoryStrip';
import { useCanWrite } from './useCanWrite';
import { NewEntryForm } from './NewEntryForm';
import { SearchBar } from './SearchBar';
import { SeriesNote } from './SeriesNote';
import { allFilter, buildKbList, type KbFilter } from './kbList';
import { kbTestIds } from './testIds';

/** The Knowledge tab: the desktop sidebar as a full screen. */
export function KnowledgeScreen() {
  const bookId = useBookId();
  const { styles, theme } = useStyles(makeStyles);
  const queryClient = useQueryClient();
  const kb = useKb(bookId);
  const canWrite = useCanWrite(bookId);
  const [query, setQuery] = useState('');
  const [filter, setFilter] = useState<KbFilter>(allFilter);
  // Set while the new-entry form is open: the category it starts in (null = the first).
  const [adding, setAdding] = useState<{ categoryId: string | null } | null>(null);
  const [refreshing, setRefreshing] = useState(false);

  const model = useMemo(
    () => (kb.data ? buildKbList(kb.data, query, filter) : null),
    [kb.data, query, filter],
  );

  async function refresh() {
    setRefreshing(true);
    try {
      await queryClient.invalidateQueries({ queryKey: kbKey(bookId) });
    } finally {
      setRefreshing(false);
    }
  }

  if (kb.isPending || kb.isError || model === null) {
    return (
      <View testID={kbTestIds.screen} style={styles.screen}>
        {kb.isError ? (
          <ErrorState
            testID={kbTestIds.error}
            error={kb.error}
            fallback="Couldn't load the Knowledge base."
            onRetry={() => void kb.refetch()}
          />
        ) : (
          <View testID={kbTestIds.loading} style={styles.centered}>
            <ActivityIndicator color={theme.colors.accent} />
          </View>
        )}
      </View>
    );
  }

  const noEntries = model.totalEntries === 0;
  return (
    <View testID={kbTestIds.screen} style={styles.screen}>
      <SearchBar
        query={query}
        total={model.totalEntries}
        onQuery={setQuery}
        onAdd={canWrite ? () => setAdding({ categoryId: null }) : undefined}
      />
      <CategoryStrip chips={model.chips} filter={filter} onFilter={setFilter} />
      <ScrollView
        testID={kbTestIds.list}
        keyboardShouldPersistTaps="handled"
        refreshControl={<RefreshControl refreshing={refreshing} onRefresh={() => void refresh()} />}
      >
        {canWrite && adding !== null && (
          <NewEntryForm
            // A different starting category is a different form.
            key={adding.categoryId ?? 'first'}
            bookId={bookId}
            categories={model.categories}
            categoryId={adding.categoryId}
            onCancel={() => setAdding(null)}
          />
        )}
        {model.sections.map((section) => (
          <View key={section.key}>
            {section.key === 'series' && <SeriesNote seriesName={section.seriesName} />}
            {section.groups.map((group) => (
              <CategoryGroup
                key={group.id}
                bookId={bookId}
                group={group}
                onAdd={canWrite ? (categoryId) => setAdding({ categoryId }) : undefined}
              />
            ))}
          </View>
        ))}
        {model.shown === 0 && noEntries && (
          <EmptyState
            testID={kbTestIds.empty}
            title="No entries yet"
            message="Characters, places and rules the book keeps track of."
            action={
              canWrite
                ? { label: 'New entry', onPress: () => setAdding({ categoryId: null }) }
                : undefined
            }
          />
        )}
        {model.shown === 0 && !noEntries && (
          <EmptyState
            testID={kbTestIds.noMatches}
            title="No entries match"
            message="Try another search or filter."
          />
        )}
        <View style={styles.tail} />
      </ScrollView>
    </View>
  );
}

function makeStyles(theme: Theme) {
  return StyleSheet.create({
    screen: { flex: 1, backgroundColor: theme.colors.bg },
    centered: { flex: 1, alignItems: 'center', justifyContent: 'center' },
    tail: { height: theme.space[5] },
  });
}
