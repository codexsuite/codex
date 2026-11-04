import { useState, type ReactNode } from 'react';
import { Modal, StyleSheet, Text, View } from 'react-native';
import { useStyles } from '~/theme/useStyles';
import type { Theme } from '~/theme';
import { Sheet } from '~/ui';
import { settingsTestIds } from './testIds';

export interface FieldSheetProps {
  title: string;
  children: ReactNode;
  /** Saves the sheet's value; a rejection keeps the sheet open and shows the server's message. */
  onSave: () => Promise<void>;
  onClose: () => void;
  /** Holds the primary back (a value that is not valid yet). */
  saveDisabled?: boolean;
  saveLabel?: string;
  /** A second dock button, left of Save (back to the series' value). */
  secondary?: { label: string; onPress: () => Promise<void> };
  /** Danger styling for the primary (Archive book). */
  danger?: boolean;
}

/**
 * A short edit as a bottom sheet over the Settings tab. The tab owns no route for it, so it is drawn in a transparent
 * `Modal` rather than pushed. Save and the optional secondary action run the call, show its error and close on success.
 */
export function FieldSheet({
  title,
  children,
  onSave,
  onClose,
  saveDisabled = false,
  saveLabel = 'Save',
  secondary,
  danger = false,
}: FieldSheetProps) {
  const { styles } = useStyles(makeStyles);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);

  const run = async (work: () => Promise<void>) => {
    setBusy(true);
    setError(null);
    try {
      await work();
      onClose();
    } catch (e) {
      setError(e instanceof Error ? e.message : 'That could not be saved.');
      setBusy(false);
    }
  };

  // Translucent bars: the backdrop covers the status and navigation bars, so the Sheet's own bottom inset is the only one.
  return (
    <Modal
      transparent
      statusBarTranslucent
      navigationBarTranslucent
      animationType="none"
      visible
      onRequestClose={onClose}
    >
      <Sheet
        title={title}
        onClose={onClose}
        testID={settingsTestIds.sheet}
        dock={{
          primary: {
            label: saveLabel,
            variant: danger ? 'danger' : 'primary',
            disabled: saveDisabled,
            busy,
            onPress: () => void run(onSave),
          },
          secondary:
            secondary === undefined
              ? { label: 'Cancel', onPress: onClose }
              : {
                  label: secondary.label,
                  onPress: () => void run(secondary.onPress),
                  disabled: busy,
                },
        }}
      >
        <View style={styles.body}>
          {children}
          {error !== null && (
            <Text
              testID={settingsTestIds.sheetError}
              accessibilityRole="alert"
              style={styles.error}
            >
              {error}
            </Text>
          )}
        </View>
      </Sheet>
    </Modal>
  );
}

function makeStyles(theme: Theme) {
  return StyleSheet.create({
    body: { gap: theme.space[3], paddingHorizontal: theme.space[4], paddingBottom: theme.space[3] },
    error: { fontSize: theme.text.sm, color: theme.colors.danger },
  });
}
