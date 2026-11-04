import type { Tabs } from 'expo-router';
import type { ComponentProps } from 'react';
import { Pressable, StyleSheet, Text, View } from 'react-native';
import { useStyles } from '../theme/useStyles';
import type { Theme } from '../theme';
import { shellTestIds } from '../ui/testIds';
import { useKeyboardVisible } from './useKeyboardVisible';

type TabBarProps = Parameters<NonNullable<ComponentProps<typeof Tabs>['tabBar']>>[0];

// TODO: draw real icons (Plan, Write, Knowledge, Settings) instead of these glyphs.
const glyphs: Record<string, string> = { plan: '☰', write: '✎', knowledge: '◉', settings: '⚙' };

/**
 * The book's bottom tab bar: `--tab-height` plus the home-indicator inset, an accent-soft pill behind the selected
 * tab's accent icon. Hidden while the keyboard is up.
 */
export function TabBar({ state, descriptors, emitter, navigateToTab, insets }: TabBarProps) {
  const { styles } = useStyles(makeStyles);
  const keyboard = useKeyboardVisible();
  if (keyboard) return null;

  return (
    <View testID={shellTestIds.tabBar} style={[styles.bar, { paddingBottom: insets.bottom }]}>
      {state.routes.map((route, index) => {
        const selected = state.index === index;
        const label = descriptors[route.key]?.options.title ?? route.name;
        const press = () => {
          const event = emitter.emit({
            type: 'tabPress',
            target: route.key,
            canPreventDefault: true,
          });
          if (!selected && !event.defaultPrevented) navigateToTab(route.key);
        };
        return (
          <Pressable
            key={route.key}
            testID={shellTestIds.tab(route.name)}
            onPress={press}
            accessibilityRole="tab"
            accessibilityState={{ selected }}
            accessibilityLabel={label}
            style={styles.tab}
          >
            <View style={[styles.pill, selected && styles.pillSelected]}>
              <Text style={[styles.glyph, selected && styles.selectedText]}>
                {glyphs[route.name] ?? '●'}
              </Text>
            </View>
            <Text style={[styles.label, selected && styles.selectedText]}>{label}</Text>
          </Pressable>
        );
      })}
    </View>
  );
}

function makeStyles(theme: Theme) {
  return StyleSheet.create({
    bar: {
      flexDirection: 'row',
      backgroundColor: theme.colors.surface,
      borderTopWidth: 1,
      borderTopColor: theme.colors.border,
    },
    tab: {
      flex: 1,
      height: theme.size.tab,
      alignItems: 'center',
      justifyContent: 'center',
      gap: 2,
    },
    pill: {
      width: 52,
      height: 26,
      borderRadius: 13,
      alignItems: 'center',
      justifyContent: 'center',
    },
    pillSelected: { backgroundColor: theme.colors['accent-soft'] },
    glyph: { fontSize: theme.text.md, color: theme.colors.muted },
    label: {
      fontSize: theme.text['2xs'],
      fontWeight: theme.weight.medium,
      color: theme.colors.muted,
    },
    selectedText: { color: theme.colors.accent },
  });
}
