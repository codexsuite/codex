import { useState } from 'react';
import { StyleSheet, Text, View } from 'react-native';
import { useStyles } from '~/theme/useStyles';
import type { Theme } from '~/theme';
import { Chip } from '~/ui';
import { FieldSheet } from './FieldSheet';
import { settingsTestIds } from './testIds';

export interface Choice<T extends string> {
  value: T;
  label: string;
  /** Shown but not pickable; `note` says why. */
  disabled?: boolean;
}

export interface ChoiceSheetProps<T extends string> {
  title: string;
  choices: readonly Choice<T>[];
  selected: T;
  /** A line under the choices (why one is unavailable). */
  note?: string;
  onSave: (value: T) => Promise<void>;
  onClose: () => void;
  secondary?: { label: string; onPress: () => Promise<void> };
}

/** Pick one of a few values (status, citation style, series number). */
export function ChoiceSheet<T extends string>({
  title,
  choices,
  selected,
  note,
  onSave,
  onClose,
  secondary,
}: ChoiceSheetProps<T>) {
  const { styles } = useStyles(makeStyles);
  const [value, setValue] = useState(selected);
  return (
    <FieldSheet
      title={title}
      onClose={onClose}
      onSave={() => (value === selected ? Promise.resolve() : onSave(value))}
      secondary={secondary}
    >
      <View style={styles.chips}>
        {choices.map((choice) => (
          <Chip
            key={choice.value}
            label={choice.label}
            selected={value === choice.value}
            onPress={choice.disabled ? undefined : () => setValue(choice.value)}
            testID={settingsTestIds.choice(choice.value)}
          />
        ))}
      </View>
      {note !== undefined && (
        <Text testID={settingsTestIds.choiceNote} style={styles.note}>
          {note}
        </Text>
      )}
    </FieldSheet>
  );
}

function makeStyles(theme: Theme) {
  return StyleSheet.create({
    chips: { flexDirection: 'row', flexWrap: 'wrap', gap: theme.space[2] },
    note: { fontSize: theme.text.sm, color: theme.colors.muted },
  });
}
