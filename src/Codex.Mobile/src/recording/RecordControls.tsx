import { StyleSheet, View } from 'react-native';
import { Button } from '~/ui';
import type { Theme } from '../theme';
import { useStyles } from '../theme/useStyles';
import type { WavRecorder } from './useWavRecorder';
import { recordTestIds } from './testIds';

/** Record, or Pause / Resume, Stop and Discard, by what the recorder is doing. */
export function RecordControls({
  recorder,
  onStop,
  onDiscard,
}: {
  recorder: WavRecorder;
  onStop: () => void;
  onDiscard: () => void;
}) {
  const { styles } = useStyles(makeStyles);
  const { status } = recorder;
  if (status === 'recording' || status === 'paused') {
    return (
      <View style={styles.row}>
        {status === 'recording' ? (
          <Button
            label="Pause"
            onPress={() => void recorder.pause()}
            testID={recordTestIds.pause}
          />
        ) : (
          <Button
            label="Resume"
            onPress={() => void recorder.resume()}
            testID={recordTestIds.resume}
          />
        )}
        <Button label="Stop" variant="danger" onPress={onStop} testID={recordTestIds.stop} />
        <Button label="Discard" onPress={onDiscard} testID={recordTestIds.discard} />
      </View>
    );
  }
  return (
    <View style={styles.row}>
      <Button
        label="Record"
        variant="primary"
        onPress={() => void recorder.start()}
        testID={recordTestIds.record}
      />
    </View>
  );
}

function makeStyles(theme: Theme) {
  return StyleSheet.create({
    row: { flexDirection: 'row', justifyContent: 'center', gap: theme.space[2] },
  });
}
