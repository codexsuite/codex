import { compactWords } from '@codex/core/features/plan/format';
import { Pressable, StyleSheet, Text, View } from 'react-native';
import { useStyles } from '~/theme/useStyles';
import type { Theme } from '~/theme';
import type { PlanSection } from './planModel';
import { planTestIds } from './testIds';

/** The sticky act header: a chevron, `Act 2`, the title, and chapters and words on the right. Tapping collapses it. */
export function ActHeader({ section, onToggle }: { section: PlanSection; onToggle: () => void }) {
  const { styles } = useStyles(makeStyles);
  const title = section.act.title.trim();
  return (
    <Pressable
      testID={planTestIds.act(section.act.id)}
      onPress={onToggle}
      accessibilityRole="button"
      accessibilityState={{ expanded: !section.collapsed }}
      style={styles.header}
    >
      <Text style={[styles.chevron, section.collapsed && styles.closed]}>{'⌄'}</Text>
      <Text style={styles.overline}>{`Act ${section.index + 1}`}</Text>
      <View style={styles.titleBox}>
        <Text style={title === '' ? styles.untitled : styles.title} numberOfLines={1}>
          {title === '' ? 'Untitled act' : title}
        </Text>
      </View>
      <Text style={styles.meta}>{`${section.chapters} ch · ${compactWords(section.words)}`}</Text>
    </Pressable>
  );
}

function makeStyles(theme: Theme) {
  return StyleSheet.create({
    header: {
      flexDirection: 'row',
      alignItems: 'center',
      gap: theme.space[2],
      height: 40,
      paddingLeft: theme.space[3],
      paddingRight: theme.space[2],
      backgroundColor: theme.colors['surface-2'],
      borderBottomWidth: 1,
      borderBottomColor: theme.colors.border,
    },
    chevron: { width: 16, fontSize: theme.text.md, color: theme.colors.muted, textAlign: 'center' },
    closed: { transform: [{ rotate: '-90deg' }] },
    overline: {
      fontSize: theme.text['2xs'],
      fontWeight: theme.weight.semibold,
      color: theme.colors.muted,
      textTransform: 'uppercase',
    },
    titleBox: { flex: 1, minWidth: 0 },
    title: { fontSize: theme.text.sm, fontWeight: theme.weight.semibold, color: theme.colors.text },
    untitled: {
      fontSize: theme.text.sm,
      fontStyle: 'italic',
      color: theme.colors.muted,
    },
    meta: { fontSize: theme.text.xs, color: theme.colors.muted },
  });
}
