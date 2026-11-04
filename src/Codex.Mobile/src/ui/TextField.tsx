import { StyleSheet, Text, TextInput, View, type TextInputProps } from 'react-native';
import { useStyles } from '../theme/useStyles';
import type { Theme } from '../theme';
import { part, shellTestIds } from './testIds';

export interface TextFieldProps extends Pick<
  TextInputProps,
  | 'value'
  | 'onChangeText'
  | 'placeholder'
  | 'multiline'
  | 'autoCapitalize'
  | 'autoCorrect'
  | 'keyboardType'
  | 'returnKeyType'
  | 'onBlur'
  | 'onFocus'
  | 'onSubmitEditing'
  | 'editable'
  | 'secureTextEntry'
> {
  /** The caption above the input. */
  label?: string;
  /** A danger note under the input. */
  error?: string | null;
  /** Minimum lines of a multiline field (default 4). */
  minLines?: number;
  testID?: string;
}

export function TextField({
  label,
  error = null,
  multiline = false,
  minLines = 4,
  testID = shellTestIds.textField,
  ...input
}: TextFieldProps) {
  const { theme, styles } = useStyles(makeStyles);
  return (
    <View style={styles.field}>
      {label !== undefined && <Text style={styles.label}>{label}</Text>}
      <TextInput
        testID={testID}
        accessibilityLabel={label}
        multiline={multiline}
        placeholderTextColor={theme.colors.muted}
        style={[
          styles.input,
          multiline && { minHeight: theme.text.md * 1.4 * minLines, textAlignVertical: 'top' },
          error !== null && styles.invalid,
        ]}
        {...input}
      />
      {error !== null && (
        <Text testID={part(testID, 'error')} accessibilityRole="alert" style={styles.error}>
          {error}
        </Text>
      )}
    </View>
  );
}

function makeStyles(theme: Theme) {
  return StyleSheet.create({
    field: { gap: theme.space[1] },
    label: { fontSize: theme.text.sm, fontWeight: theme.weight.medium, color: theme.colors.muted },
    input: {
      minHeight: theme.size.row,
      paddingHorizontal: theme.space[3],
      paddingVertical: theme.space[2],
      fontSize: theme.text.md,
      color: theme.colors.text,
      backgroundColor: theme.colors.surface,
      borderWidth: 1,
      borderColor: theme.colors.border,
      borderRadius: theme.radius.md,
    },
    invalid: { borderColor: theme.colors.danger },
    error: { fontSize: theme.text.sm, color: theme.colors.danger },
  });
}
