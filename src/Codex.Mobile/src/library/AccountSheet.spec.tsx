import { fireEvent, screen, userEvent, waitFor } from '@testing-library/react-native';
import { worldRoutes } from '~/test-support/fakes';
import { renderApp } from '~/test-support/renderScreen';
import { libraryTestIds as ids } from './testIds';
import { me } from './test-support/fakes';

async function open(api = {}) {
  const app = await renderApp({ api: { ...worldRoutes(), ...api } });
  await fireEvent.press(await screen.findByTestId(ids.avatar));
  await screen.findByTestId(ids.accountSheet);
  return app;
}

describe('Account sheet', () => {
  it('ShouldShowNameEmailAndServer', async () => {
    await open({
      'GET /api/auth/me': me({ displayName: 'Joel Dickson', email: 'joel@example.com' }),
    });

    expect(await screen.findByTestId(ids.accountName)).toHaveTextContent('Joel Dickson');
    expect(screen.getByTestId(ids.accountEmail)).toHaveTextContent('joel@example.com');
    expect(screen.getByTestId(ids.accountServer)).toHaveTextContent(/https:\/\/codex\.test/);
  });

  it('ShouldShowTheServerVersionWithoutTheBuildCommit', async () => {
    await open({
      'GET /api/auth/me': me(),
      'GET /api/health': { status: 'ok', version: '1.0.0+abc123' },
    });

    await waitFor(() =>
      expect(screen.getByTestId(ids.accountServer)).toHaveTextContent(
        /https:\/\/codex\.test · Codex 1\.0\.0$/,
      ),
    );
  });

  it('WhenTheHealthCheckFails_ShouldShowTheServerAloneWithoutAnError', async () => {
    const app = await open({ 'GET /api/auth/me': me() });

    await waitFor(() => expect(app.api.callsTo('GET /api/health')).toHaveLength(1));
    expect(screen.getByTestId(ids.accountServer)).toHaveTextContent(/https:\/\/codex\.test$/);
    expect(screen.queryByTestId(`${ids.accountSheet}-error`)).toBeNull();
  });

  it('WhenTheAccountFailsToLoad_ShouldShowAnErrorAndStillOfferSignOut', async () => {
    await open();

    expect(await screen.findByTestId(`${ids.accountSheet}-error`)).toBeOnTheScreen();
    expect(screen.getByTestId(ids.accountSignOut)).toBeOnTheScreen();
  });

  it('SpeechModelsRow_WhenPressed_ShouldPushTheSpeechModelsScreen', async () => {
    const app = await open({ 'GET /api/auth/me': me() });

    await fireEvent.press(await screen.findByTestId(ids.accountSpeech));

    expect(await screen.findByTestId('mobile-speech-screen')).toBeOnTheScreen();
    expect(app.getPathname()).toBe('/speech-models');
  });

  it('Close_ShouldReturnToTheLibrary', async () => {
    const app = await open({ 'GET /api/auth/me': me() });

    await userEvent.press(screen.getByTestId('mobile-shell-sheet-close'));

    expect(await screen.findByTestId(ids.list)).toBeOnTheScreen();
    expect(app.getPathname()).toBe('/');
  });
});
