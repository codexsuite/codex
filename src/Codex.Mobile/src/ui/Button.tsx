import { ActivityIndicator, Pressable, Text } from 'react-native';
import { StyleSheet } from 'react-native';
import { useStyles } from '../theme/useStyles';
import type { Theme } from '../theme';
import { shellTestIds } from './testIds';

export type ButtonVariant = 'primary' | 'secondary' | 'danger';

export interface ButtonProps {
  label: string;
  onPress: () => void;
  variant?: ButtonVariant;
  disabled?: boolean;
  /** Shows a spinner in place of the label and ignores presses. */
  busy?: boolean;
  /** Fills the width it is given (the dock gives each button half). */
  grow?: boolean;
  testID?: string;
}

export function Button({
  label,
  onPress,
  variant = 'secondary',
  disabled = false,
  busy = false,
  grow = false,
  testID = shellTestIds.button,
}: ButtonProps) {
  const { theme, styles } = useStyles(makeStyles);
  const inactive = disabled || busy;
  return (
    <Pressable
      testID={testID}
      onPress={inactive ? undefined : onPress}
      accessibilityRole="button"
      accessibilityState={{ disabled: inactive, busy }}
      style={[styles.base, styles[variant], grow && styles.grow, disabled && styles.disabled]}
    >
      {busy ? (
        <ActivityIndicator
          color={variant === 'secondary' ? theme.colors.accent : theme.colors['accent-fg']}
        />
      ) : (
        <Text style={[styles.label, styles[`${variant}Label`]]}>{label}</Text>
      )}
    </Pressable>
  );
}

function makeStyles(theme: Theme) {
  return StyleSheet.create({
    base: {
      height: theme.size.control + theme.space[1],
      minWidth: theme.size.control * 2,
      paddingHorizontal: theme.space[4],
      alignItems: 'center',
      justifyContent: 'center',
      borderRadius: theme.radius.md,
      borderWidth: 1,
    },
    grow: { flex: 1 },
    disabled: { opacity: 0.45 },
    primary: { backgroundColor: theme.colors.accent, borderColor: theme.colors.accent },
    secondary: { backgroundColor: theme.colors.surface, borderColor: theme.colors.border },
    danger: { backgroundColor: theme.colors.surface, borderColor: theme.colors.danger },
    label: { fontSize: theme.text.base, fontWeight: theme.weight.semibold },
    primaryLabel: { color: theme.colors['accent-fg'] },
    secondaryLabel: { color: theme.colors.text },
    dangerLabel: { color: theme.colors.danger },
  });
}
