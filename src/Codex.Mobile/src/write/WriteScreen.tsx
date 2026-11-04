import { useLocalSearchParams } from 'expo-router';
import { useMemo, useRef } from 'react';
import { ActivityIndicator, KeyboardAvoidingView, StyleSheet, View } from 'react-native';
import { useBookId } from '~/book/useBookId';
import { EmptyState, ErrorState } from '~/ui';
import { chaptersOf, resolveTarget } from './chapters';
import { writeTestIds } from './testIds';
import { useWriteData } from './useWriteData';
import { WriteEditor } from './WriteEditor';

/**
 * The Write tab: one chapter at a time in the editor page. The `sceneId` / `chapterId` params say where to open and
 * change on this same mounted screen (Open in Write, the chapter jump), so the place is derived from them on every render.
 */
export function WriteScreen() {
  const bookId = useBookId();
  return (
    <View testID={writeTestIds.screen} style={styles.fill}>
      <KeyboardAvoidingView behavior="padding" style={styles.fill}>
        <WriteBody key={bookId} bookId={bookId} />
      </KeyboardAvoidingView>
    </View>
  );
}

function WriteBody({ bookId }: { bookId: string }) {
  const params = useLocalSearchParams<{ sceneId?: string; chapterId?: string }>();
  const { sceneId, chapterId } = params;
  // A new params object means Write was navigated to (also to the place it already shows): scroll there again.
  const seenParams = useRef(params);
  const linkNonce = useRef(0);
  if (seenParams.current !== params) {
    seenParams.current = params;
    linkNonce.current += 1;
  }
  const state = useWriteData(bookId);
  const outline = state.status === 'ready' ? state.data.outline : undefined;
  const chapters = useMemo(() => (outline ? chaptersOf(outline) : []), [outline]);

  if (state.status === 'loading') {
    return (
      <View style={styles.center}>
        <ActivityIndicator testID={writeTestIds.loading} />
      </View>
    );
  }
  if (state.status === 'error') {
    return (
      <ErrorState
        testID={writeTestIds.error}
        error={state.error}
        fallback="Couldn't load this book."
        onRetry={state.retry}
      />
    );
  }
  const target = resolveTarget(chapters, { sceneId, chapterId });
  if (!target) {
    return <EmptyState testID={writeTestIds.empty} title="This book has no chapters yet." />;
  }
  return (
    <WriteEditor
      bookId={bookId}
      chapters={chapters}
      chapter={target.chapter}
      scrollToSceneId={target.scrollToSceneId}
      linkNonce={linkNonce.current}
      readOnly={state.readOnly}
    />
  );
}

const styles = StyleSheet.create({
  fill: { flex: 1 },
  center: { flex: 1, alignItems: 'center', justifyContent: 'center' },
});
