import type { OutlineChapter } from '@codex/core/api/structure';
import { compactWords } from '@codex/core/features/plan/format';
import { StyleSheet, Text, View } from 'react-native';
import { useStyles } from '~/theme/useStyles';
import type { Theme } from '~/theme';
import { planTestIds } from './testIds';

/** A chapter heading between scene cards: `Ch 3`, the title and its scene and word counts. */
export function ChapterHeading({
  chapter,
  scenes,
  words,
}: {
  chapter: OutlineChapter;
  scenes: number;
  words: number;
}) {
  const { styles } = useStyles(makeStyles);
  return (
    <View testID={planTestIds.chapter(chapter.id)} style={styles.row}>
      {chapter.number !== null && <Text style={styles.number}>{`Ch ${chapter.number}`}</Text>}
      <Text accessibilityRole="header" style={styles.title} numberOfLines={1}>
        {chapter.title}
      </Text>
      <Text
        style={styles.meta}
      >{`${scenes} ${scenes === 1 ? 'scene' : 'scenes'} · ${compactWords(words)}`}</Text>
    </View>
  );
}

function makeStyles(theme: Theme) {
  return StyleSheet.create({
    row: {
      flexDirection: 'row',
      alignItems: 'baseline',
      gap: theme.space[2],
      paddingHorizontal: theme.space[4],
      paddingTop: theme.space[3],
      paddingBottom: theme.space[1],
    },
    number: {
      fontSize: theme.text['2xs'],
      fontWeight: theme.weight.semibold,
      color: theme.colors.muted,
      textTransform: 'uppercase',
    },
    title: {
      flex: 1,
      minWidth: 0,
      fontSize: theme.text.md,
      fontWeight: theme.weight.semibold,
      color: theme.colors.text,
    },
    meta: { fontSize: theme.text.xs, color: theme.colors.muted },
  });
}
