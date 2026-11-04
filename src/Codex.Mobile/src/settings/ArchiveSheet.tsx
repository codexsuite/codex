import { StyleSheet, Text } from 'react-native';
import { useStyles } from '~/theme/useStyles';
import type { Theme } from '~/theme';
import { FieldSheet } from './FieldSheet';

/** Confirms archiving: the book leaves Library, nothing is deleted, and Restore brings it back. */
export function ArchiveSheet({
  title,
  seriesName,
  onConfirm,
  onClose,
}: {
  title: string;
  seriesName: string | null;
  onConfirm: () => Promise<void>;
  onClose: () => void;
}) {
  const { styles } = useStyles(makeStyles);
  return (
    <FieldSheet
      title={`Archive ${title}?`}
      saveLabel="Archive book"
      danger
      onSave={onConfirm}
      onClose={onClose}
    >
      <Text style={styles.text}>
        It leaves Library. Its text, Knowledge base, References and exports are kept, and you can
        restore it on the web at any time.
      </Text>
      {seriesName !== null && (
        <Text style={styles.muted}>
          {seriesName} keeps its number for it, so the other books aren’t renumbered.
        </Text>
      )}
    </FieldSheet>
  );
}

function makeStyles(theme: Theme) {
  return StyleSheet.create({
    text: { fontSize: theme.text.base, color: theme.colors.text },
    muted: { fontSize: theme.text.sm, color: theme.colors.muted },
  });
}
