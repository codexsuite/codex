import { StyleSheet, Text } from 'react-native';
import { useStyles } from '~/theme/useStyles';
import type { Theme } from '~/theme';
import { FieldSheet } from './FieldSheet';

/** Confirms restoring an archived book to Library. */
export function RestoreSheet({
  title,
  onConfirm,
  onClose,
}: {
  title: string;
  onConfirm: () => Promise<void>;
  onClose: () => void;
}) {
  const { styles } = useStyles(makeStyles);
  return (
    <FieldSheet
      title={`Restore ${title}?`}
      saveLabel="Restore book"
      onSave={onConfirm}
      onClose={onClose}
    >
      <Text style={styles.text}>It comes back to Library with everything it had.</Text>
    </FieldSheet>
  );
}

function makeStyles(theme: Theme) {
  return StyleSheet.create({ text: { fontSize: theme.text.base, color: theme.colors.text } });
}
