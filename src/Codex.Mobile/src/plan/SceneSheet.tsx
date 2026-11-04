import type { OutlineScene } from '@codex/core/api/structure';
import { parseBookRole, hasRole } from '@codex/core/api/bookRole';
import { router, useLocalSearchParams } from 'expo-router';
import { ActivityIndicator, ScrollView, StyleSheet, Text, View } from 'react-native';
import { useBookHeader, useOutline } from '~/book/queries';
import { useBookId } from '~/book/useBookId';
import { recordLink, writeLink } from '~/links';
import { useStyles } from '~/theme/useStyles';
import type { Theme } from '~/theme';
import { EmptyState, ErrorState, Sheet } from '~/ui';
import { findScene, sceneHeading } from './planModel';
import { SceneEntriesField } from './SceneEntriesField';
import { SceneLabelsField } from './SceneLabelsField';
import { SceneSummaryField } from './SceneSummaryField';
import { SceneTitleField } from './SceneTitleField';
import { planTestIds } from './testIds';

/**
 * The scene sheet: title, summary, labels and Knowledge base entries edited in place, with Record and Open in Write in
 * the dock. A route of its own (over the tab you are on), so it opens over Plan or Write.
 */
export function SceneSheet() {
  const { styles } = useStyles(makeStyles);
  const bookId = useBookId();
  const { sceneId } = useLocalSearchParams<{ sceneId: string }>();
  const outline = useOutline(bookId);
  const header = useBookHeader(bookId);
  const scene = outline.data ? findScene(outline.data, sceneId) : undefined;
  const editable = header.data !== undefined && hasRole(parseBookRole(header.data.role), 'write');

  if (scene === undefined) {
    return (
      <Sheet title="Scene">
        <View testID={planTestIds.sheet} style={styles.state}>
          {outline.isPending && <ActivityIndicator testID={planTestIds.sheetLoading} />}
          {outline.isError && (
            <ErrorState
              testID={planTestIds.sheetError}
              error={outline.error}
              fallback="Couldn't load the scene."
              onRetry={() => void outline.refetch()}
            />
          )}
          {outline.isSuccess && (
            <EmptyState
              testID={planTestIds.sheetMissing}
              title="This scene is not in the outline."
              message="It may have been archived or moved."
            />
          )}
        </View>
      </Sheet>
    );
  }

  return (
    <Sheet
      title={sceneHeading(scene.number)}
      dock={{
        secondary: {
          label: 'Open in Write',
          testID: planTestIds.sheetOpenInWrite,
          onPress: () => openInWrite(bookId, scene),
        },
        primary: {
          label: 'Record',
          testID: planTestIds.sheetRecord,
          disabled: !editable,
          onPress: () => router.push(recordLink(bookId, { kind: 'scene', id: scene.id })),
        },
      }}
    >
      <ScrollView
        testID={planTestIds.sheet}
        keyboardShouldPersistTaps="handled"
        contentContainerStyle={styles.content}
      >
        <SceneTitleField
          key={`title-${scene.id}`}
          bookId={bookId}
          scene={scene}
          editable={editable}
        />
        <SceneSummaryField
          key={`summary-${scene.id}`}
          bookId={bookId}
          scene={scene}
          editable={editable}
        />
        <SceneLabelsField bookId={bookId} scene={scene} editable={editable} />
        <SceneEntriesField bookId={bookId} scene={scene} editable={editable} />
        <Text testID={planTestIds.sheetNote} style={styles.note}>
          {scene.words > 0 ? `${scene.words.toLocaleString('en-US')} words written` : 'Not started'}
          {editable ? ' · syncs 3s after you stop typing' : ''}
        </Text>
      </ScrollView>
    </Sheet>
  );
}

/** Closes the sheet, then shows the scene in Write (CONTRACTS section 4). */
function openInWrite(bookId: string, scene: OutlineScene) {
  router.dismiss();
  router.navigate(writeLink(bookId, { sceneId: scene.id }));
}

function makeStyles(theme: Theme) {
  return StyleSheet.create({
    content: { gap: theme.space[4], padding: theme.space[4] },
    state: { padding: theme.space[4] },
    note: { fontSize: theme.text.xs, color: theme.colors.muted },
  });
}
