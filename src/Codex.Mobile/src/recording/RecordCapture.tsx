import { StyleSheet, Text, View } from 'react-native';
import type { Theme } from '../theme';
import { useStyles } from '../theme/useStyles';
import { RecordControls } from './RecordControls';
import { formatTimer } from './segments';
import { recordTestIds } from './testIds';
import type { WavRecorder } from './useWavRecorder';

/** The recording part of the sheet: the timer, what went wrong with the microphone, and the controls. */
export function RecordCapture({
  recorder,
  onStop,
  onDiscard,
}: {
  recorder: WavRecorder;
  onStop: () => void;
  onDiscard: () => void;
}) {
  const { styles } = useStyles(makeStyles);
  return (
    <View style={styles.box}>
      <Text testID={recordTestIds.timer} style={styles.timer}>
        {formatTimer(recorder.seconds)}
      </Text>
      {recorder.status === 'denied' && (
        <Text testID={recordTestIds.micDenied} accessibilityRole="alert" style={styles.problem}>
          Codex needs the microphone to record. Allow it in this phone&apos;s Settings, then try
          again.
        </Text>
      )}
      {recorder.status === 'failed' && (
        <Text testID={recordTestIds.recordFailure} accessibilityRole="alert" style={styles.problem}>
          {recorder.error}
        </Text>
      )}
      <RecordControls recorder={recorder} onStop={onStop} onDiscard={onDiscard} />
      <Text style={styles.hint}>Transcribed on this phone. The audio is deleted afterwards.</Text>
    </View>
  );
}

function makeStyles(theme: Theme) {
  return StyleSheet.create({
    box: { gap: theme.space[4], padding: theme.space[4] },
    timer: {
      fontSize: theme.text.lg * 2,
      fontWeight: theme.weight.semibold,
      color: theme.colors.text,
      textAlign: 'center',
    },
    problem: { fontSize: theme.text.base, color: theme.colors.danger, textAlign: 'center' },
    hint: { fontSize: theme.text.xs, color: theme.colors.muted, textAlign: 'center' },
  });
}
