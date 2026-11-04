import type { OutlineScene } from '@codex/core/api/structure';
import { resolveScope, wholeBook, type PlanScope } from '@codex/core/features/plan/planScope';
import { additionsKey } from '@codex/core/api/write';
import { kbKey } from '@codex/core/api/kb';
import { hasRole, parseBookRole } from '@codex/core/api/bookRole';
import { useQueryClient } from '@tanstack/react-query';
import { router } from 'expo-router';
import { useMemo, useState } from 'react';
import { ActivityIndicator, RefreshControl, SectionList, StyleSheet, View } from 'react-native';
import { useBookId } from '~/book/useBookId';
import { useBookHeader, useKb, useOutline } from '~/book/queries';
import { sceneSheetLink } from '~/links';
import { useStyles } from '~/theme/useStyles';
import type { Theme } from '~/theme';
import { EmptyState, ErrorState } from '~/ui';
import { MutationError } from './MutationError';
import { NewChapterButton } from './NewChapterButton';
import { useCreateChapter } from './useCreateChapter';
import { ActHeader } from './ActHeader';
import { ChapterHeading } from './ChapterHeading';
import { buildPlanSections, entryCategories, rowKey, type PlanRow } from './planModel';
import { SceneCard } from './SceneCard';
import { ScopeBar } from './ScopeBar';
import { planTestIds } from './testIds';
import { useAdditionCounts } from './useAdditionCounts';
import { useCollapsedActs } from './useCollapsedActs';

const NO_CHAPTERS = 'No chapters yet.';

/** The Plan tab: the outline of the open book, no board and no drag. */
export function PlanScreen() {
  const { styles, theme } = useStyles(makeStyles);
  const bookId = useBookId();
  const outline = useOutline(bookId);
  const kb = useKb(bookId);
  const header = useBookHeader(bookId);
  const queryClient = useQueryClient();
  const additions = useAdditionCounts(bookId);
  const createChapter = useCreateChapter(bookId);
  const { isCollapsed, toggle } = useCollapsedActs(bookId);
  const [pickedScope, setPickedScope] = useState<PlanScope>(wholeBook);
  const [refreshing, setRefreshing] = useState(false);

  const categories = useMemo(() => (kb.data ? entryCategories(kb.data) : new Map()), [kb.data]);
  const scope = resolveScope(outline.data, pickedScope);
  const sections = useMemo(
    () => (outline.data ? buildPlanSections(outline.data, scope, isCollapsed) : []),
    [outline.data, scope, isCollapsed],
  );

  const refresh = async () => {
    setRefreshing(true);
    try {
      await Promise.all([
        outline.refetch(),
        queryClient.refetchQueries({ queryKey: additionsKey(bookId) }),
        queryClient.refetchQueries({ queryKey: kbKey(bookId) }),
      ]);
    } finally {
      setRefreshing(false);
    }
  };

  if (outline.isPending) {
    return (
      <View testID={planTestIds.screen} style={styles.center}>
        <ActivityIndicator testID={planTestIds.loading} color={theme.colors.muted} />
      </View>
    );
  }
  if (outline.isError && outline.data === undefined) {
    return (
      <View testID={planTestIds.screen} style={styles.center}>
        <ErrorState
          testID={planTestIds.error}
          error={outline.error}
          fallback="Couldn't load the outline."
          onRetry={() => void outline.refetch()}
        />
      </View>
    );
  }

  const canAdd =
    header.data !== undefined &&
    hasRole(parseBookRole(header.data.role), 'write') &&
    scope.kind !== 'chapter';
  const open = (scene: OutlineScene) => router.push(sceneSheetLink(bookId, scene.id));
  const renderRow = ({ item }: { item: PlanRow }) => {
    switch (item.kind) {
      case 'chapter':
        return <ChapterHeading chapter={item.chapter} scenes={item.scenes} words={item.words} />;
      case 'scene':
        return (
          <SceneCard
            scene={item.scene}
            additions={additions.get(item.scene.id) ?? 0}
            categories={categories}
            onPress={() => open(item.scene)}
          />
        );
      case 'empty':
        return <EmptyState testID={planTestIds.empty} title={NO_CHAPTERS} />;
    }
  };

  return (
    <View testID={planTestIds.screen} style={styles.screen}>
      {outline.data && <ScopeBar outline={outline.data} scope={scope} onScope={setPickedScope} />}
      <MutationError
        error={createChapter.error}
        fallback="Couldn't add the chapter."
        testID={planTestIds.newChapterError}
      />
      {sections.length === 0 ? (
        <EmptyState testID={planTestIds.empty} title={NO_CHAPTERS} />
      ) : (
        <SectionList
          testID={planTestIds.list}
          sections={sections}
          keyExtractor={rowKey}
          renderItem={renderRow}
          renderSectionHeader={({ section }) => (
            <ActHeader section={section} onToggle={() => toggle(section.act.id, section.index)} />
          )}
          renderSectionFooter={({ section }) =>
            canAdd && !section.collapsed ? (
              <NewChapterButton
                actId={section.act.id}
                busy={createChapter.isPending}
                onPress={() => createChapter.mutate(section.act)}
              />
            ) : null
          }
          stickySectionHeadersEnabled
          contentContainerStyle={styles.content}
          refreshControl={
            <RefreshControl refreshing={refreshing} onRefresh={() => void refresh()} />
          }
        />
      )}
    </View>
  );
}

function makeStyles(theme: Theme) {
  return StyleSheet.create({
    screen: { flex: 1, backgroundColor: theme.colors.bg },
    center: { flex: 1, justifyContent: 'center', backgroundColor: theme.colors.bg },
    content: { paddingBottom: theme.space[5] },
  });
}
