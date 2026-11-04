import { StyleSheet, Text, View } from 'react-native';
import type { RecordTarget } from '~/links';
import { Chip } from '~/ui';
import type { Theme } from '../theme';
import { useStyles } from '../theme/useStyles';
import { recordTestIds } from './testIds';

export type LandingMode = 'new-entry' | 'add-to-an-entry' | 'add-to-entry';

export const defaultLandingMode = (target: RecordTarget): LandingMode =>
  target.kind === 'scene' ? 'new-entry' : 'add-to-entry';

/** "When you stop": what the transcript becomes, by where the recording started. */
export function LandingChoices({
  target,
  mode,
  onChange,
}: {
  target: RecordTarget;
  mode: LandingMode;
  onChange: (mode: LandingMode) => void;
}) {
  const { styles } = useStyles(makeStyles);
  // TODO: make Keep as note a real choice once the server stores voice notes (audio upload); until then it is not offered.
  return (
    <View style={styles.box}>
      <Text style={styles.caption}>Save it as</Text>
      <View style={styles.row}>
        {target.kind === 'scene' ? (
          <>
            <Chip
              label="New entry"
              selected={mode === 'new-entry'}
              onPress={() => onChange('new-entry')}
              testID={recordTestIds.modeNewEntry}
            />
            <Chip
              label="Add to an entry"
              selected={mode === 'add-to-an-entry'}
              onPress={() => onChange('add-to-an-entry')}
              testID={recordTestIds.modeAddToAnEntry}
            />
          </>
        ) : (
          <>
            <Chip
              label="Add to entry"
              selected
              onPress={() => onChange('add-to-entry')}
              testID={recordTestIds.modeAddToEntry}
            />
            <Chip label="Keep as note (needs the server)" testID={recordTestIds.modeKeepAsNote} />
          </>
        )}
      </View>
    </View>
  );
}

function makeStyles(theme: Theme) {
  return StyleSheet.create({
    box: { gap: theme.space[2] },
    caption: { fontSize: theme.text.xs, color: theme.colors.muted },
    row: { flexDirection: 'row', flexWrap: 'wrap', gap: theme.space[2] },
  });
}
