import { screen, waitFor } from '@testing-library/react-native';
import { useEffect, useState } from 'react';
import { Text } from 'react-native';
import { createFakeSpeechEngine } from '~/test-support/fakeSpeechEngine';
import { renderApp, renderScreen } from '~/test-support/renderScreen';
import { realSpeechEngine } from './realSpeechEngine';
import { useSpeechEngine } from './SpeechEngineContext';

function ActiveModel() {
  const engine = useSpeechEngine();
  const [active, setActive] = useState('loading');
  useEffect(() => void engine.activeModelId().then((id) => setActive(id ?? 'none')), [engine]);
  return <Text testID="active">{active}</Text>;
}

describe('SpeechEngineContext', () => {
  it('useSpeechEngine_WithAnInjectedEngine_ShouldUseIt', async () => {
    const speechEngine = createFakeSpeechEngine({ installed: ['base.en'], active: 'base.en' });

    await renderScreen(<ActiveModel />, { speechEngine });

    await waitFor(() => expect(screen.getByTestId('active')).toHaveTextContent('base.en'));
  });

  it('useSpeechEngine_WithoutOne_ShouldUseTheRealEngineWhichHasNoActiveModelYet', async () => {
    await renderScreen(<ActiveModel />);

    await waitFor(() => expect(screen.getByTestId('active')).toHaveTextContent('none'));
  });

  it('RenderApp_WithASpeechEngine_ShouldStillRenderTheApp', async () => {
    await renderApp({ speechEngine: createFakeSpeechEngine() });

    expect(await screen.findByTestId('mobile-library-list')).toBeOnTheScreen();
  });
});

describe('real speech engine', () => {
  it('Delete_OfAModelNotInTheCatalog_ShouldRejectRatherThanThrow', async () => {
    await expect(realSpeechEngine.delete('huge')).rejects.toMatchObject({ code: 'not-installed' });
  });
});
