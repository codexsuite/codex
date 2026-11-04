import { router } from 'expo-router';
import { speechModelsLink } from '~/links';
import { EmptyState } from '~/ui';
import { recordTestIds } from './testIds';

/** Shown instead of the recorder when no speech model is downloaded and chosen. */
export function NoSpeechModel() {
  return (
    <EmptyState
      testID={recordTestIds.noModel}
      title="No speech model yet"
      message="Recordings are transcribed on this phone. Download a speech model first."
      action={{
        label: 'Choose a speech model',
        onPress: () => router.push(speechModelsLink()),
      }}
    />
  );
}
