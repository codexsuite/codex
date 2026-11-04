import { StyleSheet, Text, View } from 'react-native';
import { Button, Chip } from '~/ui';
import type { Theme } from '../theme';
import { useStyles } from '../theme/useStyles';
import { DownloadProgressBar } from './DownloadProgressBar';
import { formatMegabytes, progressPercent } from './formatBytes';
import type { DownloadProgress, SpeechModel } from './SpeechEngine';
import { speechTestIds } from './testIds';

export interface SpeechModelRowProps {
  model: SpeechModel;
  installed: boolean;
  active: boolean;
  /** Set while this screen's download of the model is running. */
  progress: DownloadProgress | undefined;
  failure: string | undefined;
  onDownload: () => void;
  onCancel: () => void;
  onChoose: () => void;
  onDelete: () => void;
}

/** One catalog model: name, size, what state it is in, and the actions that state allows. */
export function SpeechModelRow({
  model,
  installed,
  active,
  progress,
  failure,
  onDownload,
  onCancel,
  onChoose,
  onDelete,
}: SpeechModelRowProps) {
  const { styles } = useStyles(makeStyles);
  const id = model.id;
  const status = progress
    ? `Downloading ${progressPercent(progress.receivedBytes, progress.totalBytes)}%`
    : active
      ? 'Active'
      : installed
        ? 'Installed'
        : 'Not installed';
  return (
    <View testID={speechTestIds.model(id)} style={styles.row}>
      <View style={styles.head}>
        <View style={styles.titles}>
          <Text style={styles.name}>{model.name}</Text>
          <Text style={styles.detail}>
            {formatMegabytes(model.sizeBytes)} · {model.description}
          </Text>
        </View>
        <Chip label={status} selected={active} testID={speechTestIds.status(id)} />
      </View>
      {progress !== undefined && (
        <DownloadProgressBar
          receivedBytes={progress.receivedBytes}
          totalBytes={progress.totalBytes}
          testID={speechTestIds.progress(id)}
        />
      )}
      {failure !== undefined && (
        <Text testID={speechTestIds.failure(id)} style={styles.failure}>
          {failure}
        </Text>
      )}
      <View style={styles.actions}>
        {progress !== undefined ? (
          <Button label="Cancel" onPress={onCancel} testID={speechTestIds.cancel(id)} />
        ) : installed ? (
          <>
            {!active && (
              <Button
                label="Use this model"
                variant="primary"
                onPress={onChoose}
                testID={speechTestIds.use(id)}
              />
            )}
            <Button
              label="Delete"
              variant="danger"
              onPress={onDelete}
              testID={speechTestIds.remove(id)}
            />
          </>
        ) : (
          <Button
            label="Download"
            variant="primary"
            onPress={onDownload}
            testID={speechTestIds.download(id)}
          />
        )}
      </View>
    </View>
  );
}

function makeStyles(theme: Theme) {
  return StyleSheet.create({
    row: {
      gap: theme.space[2],
      padding: theme.space[4],
      borderBottomWidth: StyleSheet.hairlineWidth,
      borderBottomColor: theme.colors.border,
    },
    head: { flexDirection: 'row', alignItems: 'center', gap: theme.space[2] },
    titles: { flex: 1, gap: theme.space[1] },
    name: { fontSize: theme.text.base, fontWeight: theme.weight.medium, color: theme.colors.text },
    detail: { fontSize: theme.text.sm, color: theme.colors.muted },
    failure: { fontSize: theme.text.sm, color: theme.colors.danger },
    actions: { flexDirection: 'row', gap: theme.space[2] },
  });
}
