import { useState } from 'react';
import type { KeyboardTypeOptions } from 'react-native';
import { Chip, TextField } from '~/ui';
import { StyleSheet, Text, View } from 'react-native';
import { useStyles } from '~/theme/useStyles';
import type { Theme } from '~/theme';
import { FieldSheet } from './FieldSheet';
import { settingsTestIds } from './testIds';

export interface TextFieldSheetProps {
  title: string;
  label: string;
  initial: string;
  hint?: string;
  keyboardType?: KeyboardTypeOptions;
  /** Suggested values shown as chips; tapping one fills the field. */
  suggestions?: readonly string[];
  /** A message when `text` is not acceptable, else null. */
  validate: (text: string) => string | null;
  onSave: (text: string) => Promise<void>;
  onClose: () => void;
  secondary?: { label: string; onPress: () => Promise<void> };
}

/** One text or number field in a sheet: Save validates, sends, and shows what the server refuses. */
export function TextFieldSheet({
  title,
  label,
  initial,
  hint,
  keyboardType,
  suggestions,
  validate,
  onSave,
  onClose,
  secondary,
}: TextFieldSheetProps) {
  const { styles } = useStyles(makeStyles);
  const [text, setText] = useState(initial);
  const problem = validate(text);
  // The message appears once the reader has changed the text; Save is held back while it is not acceptable.
  const invalid = text !== initial ? problem : null;

  return (
    <FieldSheet
      title={title}
      onClose={onClose}
      onSave={() => (text === initial ? Promise.resolve() : onSave(text))}
      saveDisabled={problem !== null}
      secondary={secondary}
    >
      <TextField
        testID={settingsTestIds.field}
        label={label}
        value={text}
        onChangeText={setText}
        keyboardType={keyboardType}
        error={invalid}
        autoCorrect={false}
      />
      {suggestions !== undefined && (
        <View style={styles.chips}>
          {suggestions.map((s) => (
            <Chip
              key={s}
              label={s}
              selected={text === s}
              onPress={() => setText(s)}
              testID={settingsTestIds.choice(s)}
            />
          ))}
        </View>
      )}
      {hint !== undefined && <Text style={styles.hint}>{hint}</Text>}
    </FieldSheet>
  );
}

function makeStyles(theme: Theme) {
  return StyleSheet.create({
    chips: { flexDirection: 'row', flexWrap: 'wrap', gap: theme.space[2] },
    hint: { fontSize: theme.text.sm, color: theme.colors.muted },
  });
}
