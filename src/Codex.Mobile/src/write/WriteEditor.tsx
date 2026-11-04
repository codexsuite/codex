import { bookKey } from '@codex/core/api/books';
import { onForbidden } from '@codex/core/realtime/forbidden';
import { useFocusEffect, useRouter } from 'expo-router';
import { useQueryClient } from '@tanstack/react-query';
import { useCallback, useEffect } from 'react';
import { Linking, Pressable, StyleSheet, Text, View } from 'react-native';
import { platform } from '@codex/core/platform';
import { WebView } from 'react-native-webview';
import { chapterJumpLink, sceneSheetLink } from '~/links';
import { useTheme } from '~/theme';
import { useStyles } from '~/theme/useStyles';
import type { Theme } from '~/theme';
import { ErrorState } from '~/ui';
import { AccessoryBar } from './AccessoryBar';
import { ChapterPill } from './ChapterPill';
import { neighbours, sceneName, type WriteChapter } from './chapters';
import { decideNavigation, editorPageUrl } from './pageGuards';
import { writeTestIds } from './testIds';
import { useEditorSession } from './useEditorSession';

export interface WriteEditorProps {
  bookId: string;
  chapters: readonly WriteChapter[];
  chapter: WriteChapter;
  scrollToSceneId: string | null;
  linkNonce: number;
  readOnly: boolean;
}

/** The chapter pill, the editor page in a WebView and, while a scene has focus, the accessory bar. */
export function WriteEditor({
  bookId,
  chapters,
  chapter,
  scrollToSceneId,
  linkNonce,
  readOnly,
}: WriteEditorProps) {
  const router = useRouter();
  const queryClient = useQueryClient();
  const theme = useTheme();
  const { styles } = useStyles(makeStyles);
  const session = useEditorSession({
    bookId,
    chapter,
    scrollToSceneId,
    linkNonce,
    readOnly,
    onOpenScene: (sceneId) => router.push(sceneSheetLink(bookId, sceneId)),
  });
  const { flush } = session;

  // Leaving the tab saves what is pending.
  useFocusEffect(
    useCallback(
      () => () => {
        void flush();
      },
      [flush],
    ),
  );

  // A save refused as forbidden means the role dropped: read the header again so the page turns read-only.
  useEffect(
    () => onForbidden(() => void queryClient.invalidateQueries({ queryKey: bookKey(bookId) })),
    [queryClient, bookId],
  );

  const { previous, next } = neighbours(chapters, chapter.id);
  const editing = session.focusedSceneId !== null && !readOnly;
  const focusedScene = chapter.scenes.find((s) => s.id === session.focusedSceneId);
  const label =
    editing && focusedScene
      ? `${chapter.label} · ${sceneName(focusedScene.number)}`
      : chapter.label;
  // The params are the truth for where Write is, so ‹ › change them (the screen stays mounted).
  const goTo = (target: WriteChapter | null) =>
    target ? () => router.setParams({ chapterId: target.id, sceneId: undefined }) : null;

  return (
    <View style={styles.fill}>
      <ChapterPill
        label={label}
        editing={editing}
        canUndo={session.format.canUndo}
        canRedo={session.format.canRedo}
        onPrevious={goTo(previous)}
        onNext={goTo(next)}
        onJump={() => router.push(chapterJumpLink(bookId, chapter.id))}
        onUndo={() => session.command('undo')}
        onRedo={() => session.command('redo')}
      />
      {session.failure !== null ? (
        <ErrorState
          testID={writeTestIds.pageError}
          error={session.failure}
          fallback={session.failure}
          onRetry={session.reloadPage}
        />
      ) : (
        <WebView
          key={session.generation}
          ref={session.webViewRef}
          testID={writeTestIds.webView}
          source={{ uri: editorPageUrl() }}
          onMessage={session.onMessage}
          onLoadEnd={session.pageLoaded}
          onError={(event) => session.pageFailed(event.nativeEvent.description || PAGE_FAILED)}
          onHttpError={(event) =>
            session.pageFailed(`${PAGE_FAILED} (HTTP ${event.nativeEvent.statusCode}).`)
          }
          originWhitelist={[platform().apiOrigin()]}
          onShouldStartLoadWithRequest={(request) => {
            const decision = decideNavigation(request.url);
            if (decision === 'external') void Linking.openURL(request.url);
            return decision === 'allow';
          }}
          onContentProcessDidTerminate={session.pageProcessGone}
          onRenderProcessGone={session.pageProcessGone}
          hideKeyboardAccessoryView
          style={[styles.fill, { backgroundColor: theme.colors.surface }]}
        />
      )}
      {session.notice !== null && (
        <View style={styles.notice}>
          <Text testID={writeTestIds.notice} accessibilityRole="alert" style={styles.noticeText}>
            {session.notice}
          </Text>
          <Pressable
            testID={writeTestIds.noticeDismiss}
            onPress={session.dismissNotice}
            accessibilityRole="button"
            accessibilityLabel="Dismiss"
            hitSlop={8}
          >
            <Text style={styles.dismiss}>{'\u00D7'}</Text>
          </Pressable>
        </View>
      )}
      {editing && <AccessoryBar format={session.format} onCommand={session.command} />}
    </View>
  );
}

const PAGE_FAILED = 'The editor could not be loaded.';

function makeStyles(theme: Theme) {
  return StyleSheet.create({
    fill: { flex: 1 },
    notice: {
      flexDirection: 'row',
      alignItems: 'center',
      gap: theme.space[3],
      paddingHorizontal: theme.space[4],
      paddingVertical: theme.space[2],
      backgroundColor: theme.colors['surface-2'],
    },
    noticeText: { flex: 1, fontSize: theme.text.sm, color: theme.colors.danger },
    dismiss: { fontSize: theme.text.lg, color: theme.colors.muted },
  });
}
