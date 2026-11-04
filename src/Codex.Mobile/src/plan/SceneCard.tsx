import type { OutlineScene } from '@codex/core/api/structure';
import { plainSummary } from '@codex/core/features/plan/format';
import { Pressable, StyleSheet, Text, View } from 'react-native';
import { useStyles } from '~/theme/useStyles';
import type { CategoryName, Theme } from '~/theme';
import { EntryTag } from './EntryTag';
import { LabelDot } from './LabelDot';
import { sceneNumberInChapter } from './planModel';
import { planTestIds } from './testIds';

interface SceneCardProps {
  scene: OutlineScene;
  additions: number;
  categories: ReadonlyMap<string, CategoryName>;
  onPress: () => void;
}

/** A scene in the outline: number, title (or an italic Untitled), two lines of summary, labels, entries, words. */
export function SceneCard({ scene, additions, categories, onPress }: SceneCardProps) {
  const { styles } = useStyles(makeStyles);
  const title = scene.title?.trim() ?? '';
  const summary = plainSummary(scene.summary);
  return (
    <Pressable
      testID={planTestIds.scene(scene.id)}
      onPress={onPress}
      accessibilityRole="button"
      style={({ pressed }) => [styles.card, pressed && styles.pressed]}
    >
      <Text style={styles.number}>{sceneNumberInChapter(scene.number)}</Text>
      <View style={styles.body}>
        <Text
          testID={planTestIds.sceneTitle(scene.id)}
          style={title === '' ? styles.untitled : styles.title}
          numberOfLines={1}
        >
          {title === '' ? 'Untitled' : title}
        </Text>
        {summary !== '' && (
          <Text
            testID={planTestIds.sceneSummary(scene.id)}
            style={styles.summary}
            numberOfLines={2}
          >
            {summary}
          </Text>
        )}
        <View style={styles.footer}>
          {scene.labels.map((label) => (
            <LabelDot
              key={label.id}
              name={label.name}
              color={label.color}
              testID={planTestIds.sceneLabel(scene.id, label.id)}
            />
          ))}
          {scene.linkedEntries.map((entry) => (
            <EntryTag
              key={entry.id}
              name={entry.name}
              category={categories.get(entry.id)}
              testID={planTestIds.sceneEntry(scene.id, entry.id)}
            />
          ))}
          {additions > 0 && (
            <Text testID={planTestIds.sceneAdditions(scene.id)} style={styles.meta}>
              {`+${additions} ${additions === 1 ? 'addition' : 'additions'}`}
            </Text>
          )}
          <Text testID={planTestIds.sceneWords(scene.id)} style={[styles.meta, styles.words]}>
            {scene.words > 0 ? `${scene.words.toLocaleString('en-US')} words` : 'Not started'}
          </Text>
        </View>
      </View>
    </Pressable>
  );
}

function makeStyles(theme: Theme) {
  return StyleSheet.create({
    card: {
      flexDirection: 'row',
      gap: theme.space[3],
      marginHorizontal: theme.space[3],
      marginVertical: theme.space[1],
      paddingHorizontal: theme.space[3],
      paddingVertical: theme.space[3],
      backgroundColor: theme.colors.surface,
      borderWidth: 1,
      borderColor: theme.colors.border,
      borderRadius: theme.radius.md,
    },
    pressed: { borderColor: theme.colors.accent, backgroundColor: theme.colors['accent-soft'] },
    number: {
      width: 22,
      fontSize: theme.text.xs,
      fontWeight: theme.weight.semibold,
      color: theme.colors.muted,
    },
    body: { flex: 1, minWidth: 0, gap: theme.space[1] },
    title: {
      fontSize: theme.text.base,
      fontWeight: theme.weight.medium,
      color: theme.colors.text,
    },
    untitled: {
      fontSize: theme.text.base,
      fontStyle: 'italic',
      fontWeight: theme.weight.regular,
      color: theme.colors.muted,
    },
    summary: { fontSize: theme.text.sm, color: theme.colors.muted },
    footer: {
      flexDirection: 'row',
      flexWrap: 'wrap',
      alignItems: 'center',
      columnGap: theme.space[2],
      rowGap: theme.space[1],
    },
    meta: { fontSize: theme.text.xs, color: theme.colors.muted },
    words: { marginLeft: 'auto' },
  });
}
