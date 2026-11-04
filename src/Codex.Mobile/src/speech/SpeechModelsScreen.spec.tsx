import { act, fireEvent, screen, userEvent } from '@testing-library/react-native';
import { router } from 'expo-router';
import { Alert } from 'react-native';
import { speechModelsLink } from '~/links';
import { createFakeSpeechEngine } from '~/test-support/fakeSpeechEngine';
import { renderApp } from '~/test-support/renderScreen';
import { speechTestIds } from './testIds';

async function openScreen(options: Parameters<typeof createFakeSpeechEngine>[0] = {}) {
  const speechEngine = createFakeSpeechEngine(options);
  const app = await renderApp({ speechEngine });
  await screen.findByTestId('mobile-library-list');
  await act(async () => router.push(speechModelsLink()));
  await screen.findByTestId(speechTestIds.screen);
  return { speechEngine, app };
}

const status = (id: string) => screen.getByTestId(speechTestIds.status(id));

describe('Speech models screen', () => {
  it('ShouldListTheCatalogWithSizesAndTheHuggingFaceNote', async () => {
    await openScreen();

    expect(await screen.findByTestId(speechTestIds.model('base.en'))).toHaveTextContent(
      /Base \(English\).*148 MB/,
    );
    expect(screen.getByTestId(speechTestIds.model('small.en'))).toHaveTextContent(/488 MB/);
    expect(status('base.en')).toHaveTextContent('Not installed');
    expect(screen.getByTestId(speechTestIds.note)).toHaveTextContent(/Hugging Face.*on this phone/);
    expect(screen.getByTestId('mobile-shell-push-bar')).toHaveTextContent(/Account/);
  });

  it('ShouldShowInstalledAndActiveModels', async () => {
    await openScreen({ installed: ['tiny.en', 'base.en'], active: 'base.en' });

    expect(await screen.findByTestId(speechTestIds.status('base.en'))).toHaveTextContent('Active');
    expect(status('tiny.en')).toHaveTextContent('Installed');
    expect(screen.queryByTestId(speechTestIds.use('base.en'))).toBeNull();
    expect(screen.getByTestId(speechTestIds.use('tiny.en'))).toBeOnTheScreen();
  });

  it('Download_ShouldShowProgressThenInstallAndActivateTheFirstModel', async () => {
    const { speechEngine } = await openScreen();

    await fireEvent.press(await screen.findByTestId(speechTestIds.download('base.en')));
    expect(status('base.en')).toHaveTextContent('Downloading 0%');
    await act(async () => speechEngine.downloadOf('base.en').progress(73_982_106));

    expect(status('base.en')).toHaveTextContent('Downloading 50%');
    expect(screen.getByTestId(speechTestIds.progress('base.en'))).toHaveProp('accessibilityValue', {
      min: 0,
      max: 100,
      now: 50,
    });

    await act(async () => speechEngine.downloadOf('base.en').finish());

    expect(await screen.findByTestId(speechTestIds.remove('base.en'))).toBeOnTheScreen();
    expect(status('base.en')).toHaveTextContent('Active');
    expect(screen.queryByTestId(speechTestIds.progress('base.en'))).toBeNull();
  });

  it('Cancel_ShouldStopTheDownloadAndOfferItAgain', async () => {
    const { speechEngine } = await openScreen();
    await fireEvent.press(await screen.findByTestId(speechTestIds.download('tiny.en')));

    await fireEvent.press(screen.getByTestId(speechTestIds.cancel('tiny.en')));

    expect(await screen.findByTestId(speechTestIds.download('tiny.en'))).toBeOnTheScreen();
    expect(status('tiny.en')).toHaveTextContent('Not installed');
    expect(screen.queryByTestId(speechTestIds.failure('tiny.en'))).toBeNull();
    expect(() => speechEngine.downloadOf('tiny.en')).toThrow();
  });

  it('Download_WhenItFails_ShouldShowTheErrorAndOfferItAgain', async () => {
    const { speechEngine } = await openScreen();
    await fireEvent.press(await screen.findByTestId(speechTestIds.download('tiny.en')));

    await act(async () => speechEngine.downloadOf('tiny.en').fail('No connection.'));

    expect(await screen.findByTestId(speechTestIds.failure('tiny.en'))).toHaveTextContent(
      'No connection.',
    );
    expect(screen.getByTestId(speechTestIds.download('tiny.en'))).toBeOnTheScreen();
  });

  it('Delete_ShouldAskFirstAndThenRemoveTheModel', async () => {
    const alert = jest.spyOn(Alert, 'alert').mockImplementation(() => {});
    const { speechEngine } = await openScreen({ installed: ['tiny.en'], active: 'tiny.en' });

    await fireEvent.press(await screen.findByTestId(speechTestIds.remove('tiny.en')));

    expect(alert).toHaveBeenCalledWith(
      'Delete Tiny (English)?',
      expect.any(String),
      expect.any(Array),
    );
    expect(await speechEngine.installed()).toHaveLength(1);

    const buttons = alert.mock.calls[0]![2]!;
    await act(async () => buttons.find((b) => b.style === 'destructive')!.onPress!());

    expect(await screen.findByTestId(speechTestIds.download('tiny.en'))).toBeOnTheScreen();
    expect(await speechEngine.installed()).toEqual([]);
    expect(await speechEngine.activeModelId()).toBeNull();
  });

  it('Delete_WhenCancelledInTheDialog_ShouldKeepTheModel', async () => {
    const alert = jest.spyOn(Alert, 'alert').mockImplementation(() => {});
    const { speechEngine } = await openScreen({ installed: ['tiny.en'], active: 'tiny.en' });
    await fireEvent.press(await screen.findByTestId(speechTestIds.remove('tiny.en')));

    const buttons = alert.mock.calls[0]![2]!;
    expect(buttons.find((b) => b.style === 'cancel')!.onPress).toBeUndefined();

    expect(await speechEngine.installed()).toHaveLength(1);
  });

  it('UseThisModel_ShouldMakeItActive', async () => {
    const { speechEngine } = await openScreen({
      installed: ['tiny.en', 'base.en'],
      active: 'base.en',
    });

    await userEvent.press(await screen.findByTestId(speechTestIds.use('tiny.en')));

    expect(await screen.findByTestId(speechTestIds.use('base.en'))).toBeOnTheScreen();
    expect(status('tiny.en')).toHaveTextContent('Active');
    expect(await speechEngine.activeModelId()).toBe('tiny.en');
  });

  it('LeavingAndReturningMidDownload_ShouldShowTheRunningProgress', async () => {
    const { speechEngine } = await openScreen();
    await fireEvent.press(await screen.findByTestId(speechTestIds.download('base.en')));
    await fireEvent.press(screen.getByTestId('mobile-shell-push-bar-back'));
    await screen.findByTestId('mobile-library-list');

    await act(async () => speechEngine.downloadOf('base.en').progress(73_982_106));
    await act(async () => router.push(speechModelsLink()));

    expect(await screen.findByTestId(speechTestIds.status('base.en'))).toHaveTextContent(
      'Downloading 50%',
    );
    expect(screen.getByTestId(speechTestIds.cancel('base.en'))).toBeOnTheScreen();

    await act(async () => speechEngine.downloadOf('base.en').finish());
    expect(await screen.findByTestId(speechTestIds.remove('base.en'))).toBeOnTheScreen();
  });

  it('ADownloadThatFailedWhileAway_ShouldShowItsErrorOnReturn', async () => {
    const { speechEngine } = await openScreen();
    await fireEvent.press(await screen.findByTestId(speechTestIds.download('base.en')));
    await fireEvent.press(screen.getByTestId('mobile-shell-push-bar-back'));
    await screen.findByTestId('mobile-library-list');

    await act(async () => speechEngine.downloadOf('base.en').fail('Connection lost.'));
    await act(async () => router.push(speechModelsLink()));

    expect(await screen.findByTestId(speechTestIds.failure('base.en'))).toHaveTextContent(
      'Connection lost.',
    );
    expect(screen.getByTestId(speechTestIds.download('base.en'))).toBeOnTheScreen();
  });

  it('Back_ShouldReturnToTheScreenItWasOpenedFrom', async () => {
    const app = await openScreen();

    await fireEvent.press(screen.getByTestId('mobile-shell-push-bar-back'));

    expect(await screen.findByTestId('mobile-library-list')).toBeOnTheScreen();
    expect(app.app.getPathname()).toBe('/');
  });
});
