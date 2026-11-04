import type { Outline } from '@codex/core/api/structure';
import {
  scopeOptions,
  scopeValue,
  parseScope,
  scopeWords,
  type PlanScope,
} from '@codex/core/features/plan/planScope';
import { countsLabel } from '@codex/core/features/write/scope';
import { useState } from 'react';
import { Pressable, ScrollView, StyleSheet, Text, View } from 'react-native';
import { useStyles } from '~/theme/useStyles';
import type { Theme } from '~/theme';
import { planTestIds } from './testIds';

interface ScopeBarProps {
  outline: Outline;
  /** Already resolved against the outline (a scope that is gone is the whole book). */
  scope: PlanScope;
  onScope: (scope: PlanScope) => void;
}

/** Under the book bar: the scope pill (whole book, an act or a chapter) and the words, pages and read time in scope. */
export function ScopeBar({ outline, scope, onScope }: ScopeBarProps) {
  const { styles } = useStyles(makeStyles);
  const [open, setOpen] = useState(false);
  const options = scopeOptions(outline);
  const current = scopeValue(scope);
  const label = options.find((option) => option.value === current)?.label ?? 'Full manuscript';
  return (
    <View>
      <View style={styles.bar}>
        <Pressable
          testID={planTestIds.scopePill}
          onPress={() => setOpen((o) => !o)}
          accessibilityRole="button"
          accessibilityState={{ expanded: open }}
          style={styles.pill}
        >
          <Text style={styles.pillLabel} numberOfLines={1}>
            {label}
          </Text>
          <Text style={styles.pillChevron}>{'⌄'}</Text>
        </Pressable>
        <Text testID={planTestIds.counts} style={styles.counts} numberOfLines={1}>
          {countsLabel({ words: scopeWords(outline, scope) })}
        </Text>
      </View>
      {open && (
        <ScrollView
          testID={planTestIds.scopeList}
          style={styles.list}
          keyboardShouldPersistTaps="handled"
        >
          {options.map((option) => (
            <Pressable
              key={option.value}
              testID={planTestIds.scopeOption(option.value)}
              onPress={() => {
                onScope(parseScope(option.value));
                setOpen(false);
              }}
              accessibilityRole="button"
              accessibilityState={{ selected: option.value === current }}
              style={[styles.option, option.value === current && styles.selected]}
            >
              <Text style={styles.optionLabel} numberOfLines={1}>
                {option.label}
              </Text>
            </Pressable>
          ))}
        </ScrollView>
      )}
    </View>
  );
}

function makeStyles(theme: Theme) {
  return StyleSheet.create({
    bar: {
      flexDirection: 'row',
      alignItems: 'center',
      gap: theme.space[2],
      paddingHorizontal: theme.space[3],
      paddingVertical: theme.space[2],
      backgroundColor: theme.colors.surface,
      borderBottomWidth: 1,
      borderBottomColor: theme.colors.border,
    },
    pill: {
      flexShrink: 1,
      flexDirection: 'row',
      alignItems: 'center',
      gap: theme.space[1],
      height: 30,
      paddingHorizontal: theme.space[3],
      borderRadius: 15,
      backgroundColor: theme.colors['surface-2'],
    },
    pillLabel: {
      flexShrink: 1,
      fontSize: theme.text.sm,
      fontWeight: theme.weight.medium,
      color: theme.colors.text,
    },
    pillChevron: { fontSize: theme.text.sm, color: theme.colors.muted },
    counts: { flex: 1, minWidth: 0, fontSize: theme.text.xs, color: theme.colors.muted },
    list: {
      maxHeight: 260,
      backgroundColor: theme.colors.surface,
      borderBottomWidth: 1,
      borderBottomColor: theme.colors.border,
    },
    option: {
      minHeight: theme.size.row,
      justifyContent: 'center',
      paddingHorizontal: theme.space[4],
    },
    selected: { backgroundColor: theme.colors['accent-soft'] },
    optionLabel: { fontSize: theme.text.base, color: theme.colors.text },
  });
}
