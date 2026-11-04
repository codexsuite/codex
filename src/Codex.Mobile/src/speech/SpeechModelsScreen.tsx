import { Alert, ScrollView, StyleSheet, Text, View } from 'react-native';
import { ErrorState, PushBar } from '~/ui';
import type { Theme } from '../theme';
import { useStyles } from '../theme/useStyles';
import type { SpeechModel } from './SpeechEngine';
import { useSpeechEngine } from './SpeechEngineContext';
import { SpeechModelRow } from './SpeechModelRow';
import { speechTestIds } from './testIds';
import { useSpeechModels } from './useSpeechModels';

const confirmDelete = (model: SpeechModel, onConfirm: () => void) =>
  Alert.alert(`Delete ${model.name}?`, 'You can download it again later.', [
    { text: 'Cancel', style: 'cancel' },
    { text: 'Delete', style: 'destructive', onPress: onConfirm },
  ]);

/** Pushed from the Account sheet: download the speech models, choose the active one, delete the rest. */
export function SpeechModelsScreen() {
  const { styles } = useStyles(makeStyles);
  const catalog = useSpeechEngine().catalog();
  const models = useSpeechModels();
  return (
    <View testID={speechTestIds.screen} style={styles.screen}>
      <PushBar parent="Account" title="Speech models" />
      <ScrollView>
        <Text testID={speechTestIds.note} style={styles.note}>
          Models download from Hugging Face and run on this phone. Recordings are transcribed here
          and the audio never leaves the phone.
        </Text>
        {models.loadError !== null && (
          <ErrorState
            error={new Error(models.loadError)}
            fallback="The speech models could not be read."
            onRetry={models.reload}
            testID={speechTestIds.error}
          />
        )}
        {!models.loading &&
          catalog.map((model) => (
            <SpeechModelRow
              key={model.id}
              model={model}
              installed={models.installedIds.has(model.id)}
              active={models.activeId === model.id}
              progress={models.progress[model.id]}
              failure={models.failures[model.id]}
              onDownload={() => models.download(model.id)}
              onCancel={() => models.cancel(model.id)}
              onChoose={() => models.choose(model.id)}
              onDelete={() => confirmDelete(model, () => models.remove(model.id))}
            />
          ))}
      </ScrollView>
    </View>
  );
}

function makeStyles(theme: Theme) {
  return StyleSheet.create({
    screen: { flex: 1, backgroundColor: theme.colors.bg },
    note: { padding: theme.space[4], fontSize: theme.text.sm, color: theme.colors.muted },
  });
}
