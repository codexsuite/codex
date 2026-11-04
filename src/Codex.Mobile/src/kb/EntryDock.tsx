import { StyleSheet, View } from 'react-native';
import { useStyles } from '~/theme/useStyles';
import type { Theme } from '~/theme';
import { Button } from '~/ui';
import { kbTestIds } from './testIds';

/** Scenes (scrolls to Used in) and Record to entry (primary). */
export function EntryDock({
  onScenes,
  onRecord,
  canRecord,
}: {
  onScenes: () => void;
  onRecord: () => void;
  /** Recording appends to the summary, which takes the write role. */
  canRecord: boolean;
}) {
  const { styles } = useStyles(makeStyles);
  return (
    <View style={styles.dock}>
      <Button testID={kbTestIds.dockScenes} label="Scenes" onPress={onScenes} grow />
      <Button
        testID={kbTestIds.dockRecord}
        label="Record to entry"
        variant="primary"
        onPress={onRecord}
        disabled={!canRecord}
        grow
      />
    </View>
  );
}

function makeStyles(theme: Theme) {
  return StyleSheet.create({
    dock: {
      flexDirection: 'row',
      gap: theme.space[2],
      padding: theme.space[3],
      backgroundColor: theme.colors.surface,
      borderTopWidth: StyleSheet.hairlineWidth,
      borderTopColor: theme.colors.border,
    },
  });
}
