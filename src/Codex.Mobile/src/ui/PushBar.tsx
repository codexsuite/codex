import { router } from 'expo-router';
import { Pressable, StyleSheet, Text, View } from 'react-native';
import { useSafeAreaInsets } from 'react-native-safe-area-context';
import { useStyles } from '../theme/useStyles';
import type { Theme } from '../theme';
import { part, shellTestIds } from './testIds';

export interface PushBarProps {
  /** The screen one level up, shown after the ‹ (`Knowledge`, `Settings`). */
  parent: string;
  /** Centred. */
  title: string;
  /** Back one level. Defaults to `router.back()`. */
  onBack?: () => void;
  /** The optional text action at the end (`Done`). */
  action?: { label: string; onPress: () => void };
  testID?: string;
}

/** The bar of a pushed screen: ‹ Parent, a centred title, and an optional action. Includes the top safe-area inset. */
export function PushBar({
  parent,
  title,
  onBack = () => router.back(),
  action,
  testID = shellTestIds.pushBar,
}: PushBarProps) {
  const { styles } = useStyles(makeStyles);
  const insets = useSafeAreaInsets();
  return (
    <View style={[styles.wrap, { paddingTop: insets.top }]}>
      <View testID={testID} style={styles.bar}>
        <Pressable
          testID={part(testID, 'back')}
          onPress={onBack}
          hitSlop={8}
          accessibilityRole="button"
          accessibilityLabel={`Back to ${parent}`}
          style={styles.side}
        >
          <Text style={styles.chevron}>{'‹'}</Text>
          <Text style={styles.backLabel} numberOfLines={1}>
            {parent}
          </Text>
        </Pressable>
        <Text
          testID={part(testID, 'title')}
          accessibilityRole="header"
          style={styles.title}
          numberOfLines={1}
        >
          {title}
        </Text>
        <View style={[styles.side, styles.end]}>
          {action !== undefined && (
            <Pressable
              testID={part(testID, 'action')}
              onPress={action.onPress}
              hitSlop={8}
              accessibilityRole="button"
            >
              <Text style={styles.action}>{action.label}</Text>
            </Pressable>
          )}
        </View>
      </View>
    </View>
  );
}

function makeStyles(theme: Theme) {
  return StyleSheet.create({
    wrap: {
      backgroundColor: theme.colors.surface,
      borderBottomWidth: 1,
      borderBottomColor: theme.colors.border,
    },
    bar: {
      height: theme.size.bar,
      flexDirection: 'row',
      alignItems: 'center',
      paddingHorizontal: theme.space[2],
    },
    side: { flex: 1, flexDirection: 'row', alignItems: 'center' },
    end: { justifyContent: 'flex-end', paddingRight: theme.space[2] },
    chevron: { fontSize: 32, lineHeight: 34, color: theme.colors.accent, marginRight: 2 },
    backLabel: { flexShrink: 1, fontSize: theme.text.base, color: theme.colors.accent },
    title: {
      flex: 2,
      textAlign: 'center',
      fontSize: theme.text.md,
      fontWeight: theme.weight.semibold,
      color: theme.colors.text,
    },
    action: {
      fontSize: theme.text.base,
      fontWeight: theme.weight.semibold,
      color: theme.colors.accent,
    },
  });
}
