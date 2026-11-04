import { router, useLocalSearchParams } from 'expo-router';
import { ActivityIndicator, ScrollView, StyleSheet, View } from 'react-native';
import { useOutline } from '~/book/queries';
import { useBookId } from '~/book/useBookId';
import { writeLink } from '~/links';
import { useStyles } from '~/theme/useStyles';
import type { Theme } from '~/theme';
import { EmptyState, ErrorState, Row, SectionHeader, Sheet } from '~/ui';
import { actsOf } from './chapters';
import { writeTestIds } from './testIds';

/** The chapter jump: every live chapter grouped by act, the one being read marked. Picking one opens Write at it. */
export function ChapterJumpSheet() {
  const bookId = useBookId();
  const { chapterId } = useLocalSearchParams<{ chapterId?: string }>();
  const outline = useOutline(bookId);
  const { styles } = useStyles(makeStyles);

  const pick = (id: string) => {
    router.dismiss();
    router.navigate(writeLink(bookId, { chapterId: id }));
  };

  const acts = outline.data ? actsOf(outline.data) : [];
  return (
    <Sheet title="Jump to chapter" testID={writeTestIds.chapterJumpSheet}>
      {outline.isError ? (
        <ErrorState
          testID={writeTestIds.chapterJumpError}
          error={outline.error}
          fallback="Couldn't load the chapters."
          onRetry={() => void outline.refetch()}
        />
      ) : !outline.data ? (
        <ActivityIndicator />
      ) : acts.length === 0 ? (
        <EmptyState testID={writeTestIds.chapterJumpEmpty} title="This book has no chapters yet." />
      ) : (
        <ScrollView>
          {acts.map((act) => (
            <View key={act.id} style={styles.act}>
              <SectionHeader
                testID={writeTestIds.chapterJumpAct(act.id)}
                title={act.title}
                count={act.chapters.length}
              />
              {act.chapters.map((chapter) => (
                <Row
                  key={chapter.id}
                  testID={writeTestIds.chapterJumpRow(chapter.id)}
                  label={chapter.label}
                  value={`${chapter.words.toLocaleString('en-US')} words`}
                  chip={chapter.id === chapterId ? 'Reading' : undefined}
                  onPress={() => pick(chapter.id)}
                />
              ))}
            </View>
          ))}
        </ScrollView>
      )}
    </Sheet>
  );
}

function makeStyles(theme: Theme) {
  return StyleSheet.create({ act: { paddingBottom: theme.space[2] } });
}
