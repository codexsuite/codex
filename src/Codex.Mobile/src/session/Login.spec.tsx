import { act, fireEvent, screen, userEvent } from '@testing-library/react-native';
import Login from '../../app/login';
import { installFakeApi, type FakeApi } from '~/test-support/fakeApi';
import { renderScreen } from '~/test-support/renderScreen';

const SERVER = 'mobile-login-server';
const STATUS = 'mobile-login-server-status';
const SUBMIT = 'mobile-login-submit';
const GOOD = { status: 'ok', version: '1.0.0+abc123' };

const healthUrls = () =>
  (globalThis.fetch as jest.Mock).mock.calls
    .map((call) => String(call[0]))
    .filter((url) => url.endsWith('/api/health'));

async function typeServer(text: string) {
  await fireEvent.changeText(await screen.findByTestId(SERVER), text);
}

async function leaveServer() {
  await fireEvent(screen.getByTestId(SERVER), 'blur');
}

describe('Sign in server check', () => {
  let api: FakeApi;

  async function open(routes: Record<string, unknown> = { 'GET /api/health': GOOD }) {
    api = installFakeApi(routes);
    await renderScreen(<Login />);
  }

  it('ShouldCheckTheStoredServerOnOpenAndEnableSignIn', async () => {
    await open();

    expect(await screen.findByText('Codex 1.0.0 · connected')).toBeOnTheScreen();
    expect(healthUrls()).toEqual(['https://codex.test/api/health']);
    expect(screen.getByTestId(SUBMIT)).toBeEnabled();
  });

  it('ShouldCheckOnLeavingTheFieldWithHttpsAddedAndEnableSignIn', async () => {
    await open({ 'GET /api/health': GOOD });
    await typeServer('codex.example.com');
    expect(screen.getByTestId(SUBMIT)).toBeDisabled();

    await leaveServer();

    expect(await screen.findByText('Codex 1.0.0 · connected')).toBeOnTheScreen();
    expect(healthUrls().at(-1)).toBe('https://codex.example.com/api/health');
    expect(screen.getByTestId(SUBMIT)).toBeEnabled();
  });

  it('ShouldShowCantReachAndKeepSignInDisabledWhenTheServerAnswersWithAnError', async () => {
    await open({ 'GET /api/health': { status: 503, body: { error: 'down' } } });
    await typeServer('codex.exmaple.com');
    await leaveServer();

    expect(await screen.findByTestId(STATUS)).toHaveTextContent(
      "Can't reach codex.exmaple.com. Check the address and that the server is running.",
    );
    expect(screen.getByTestId(SUBMIT)).toBeDisabled();
    await userEvent.press(screen.getByTestId(SUBMIT));
    expect(api.callsTo('POST /api/auth/device-sessions')).toHaveLength(0);
  });

  it('ShouldShowCantReachWhenTheRequestFailsOutright', async () => {
    await open({
      'GET /api/health': () => {
        throw new TypeError('Network request failed');
      },
    });

    expect(await screen.findByText(/Can't reach codex\.test/)).toBeOnTheScreen();
    expect(screen.getByTestId(SUBMIT)).toBeDisabled();
  });

  it('ShouldRefuseAServerWhoseHealthIsNotCodexs', async () => {
    await open({ 'GET /api/health': { status: 'UP' } });

    expect(await screen.findByTestId(STATUS)).toHaveTextContent(
      "codex.test isn't a Codex server. Check the address.",
    );
    expect(screen.getByTestId(SUBMIT)).toBeDisabled();
  });

  it('ShouldResetTheCheckWhenTheAddressIsEdited', async () => {
    await open();
    expect(await screen.findByText('Codex 1.0.0 · connected')).toBeOnTheScreen();

    await typeServer('codex.other.com');

    expect(screen.queryByTestId(STATUS)).toBeNull();
    expect(screen.getByTestId(SUBMIT)).toBeDisabled();
  });

  it('ShouldIgnoreTheAnswerOfACheckForAnAddressThatWasEditedAway', async () => {
    let release: () => void = () => undefined;
    const held = new Promise<void>((resolve) => (release = resolve));
    await open({ 'GET /api/health': GOOD });
    const original = globalThis.fetch;
    globalThis.fetch = jest.fn(async (input: unknown, init?: RequestInit) => {
      await held;
      return original(input as string, init);
    }) as unknown as typeof fetch;
    try {
      await typeServer('slow.example.com');
      await leaveServer();
      expect(await screen.findByText('Checking slow.example.com…')).toBeOnTheScreen();

      await typeServer('other.example.com');
      await act(async () => release());

      expect(screen.queryByTestId(STATUS)).toBeNull();
      expect(screen.getByTestId(SUBMIT)).toBeDisabled();
    } finally {
      globalThis.fetch = original;
    }
  });

  it('ShouldNotSignInFromThePasswordFieldWhenTheCheckFails', async () => {
    await open({ 'GET /api/health': { status: 500, body: {} } });
    await typeServer('codex.exmaple.com');

    await fireEvent(screen.getByTestId('mobile-login-password'), 'submitEditing');

    expect(await screen.findByTestId(STATUS)).toHaveTextContent(/Can't reach codex\.exmaple\.com/);
    expect(api.callsTo('POST /api/auth/device-sessions')).toHaveLength(0);
  });

  it('ShouldKeepTheRefusedSignInMessageAndTheServerAsTyped', async () => {
    await open({
      'GET /api/health': GOOD,
      'POST /api/auth/device-sessions': {
        status: 401,
        body: {
          error: 'Incorrect email or password, or the account is locked for now.',
          code: 'unauthorized',
        },
      },
    });
    expect(await screen.findByText('Codex 1.0.0 · connected')).toBeOnTheScreen();
    await fireEvent.changeText(screen.getByTestId('mobile-login-email'), 'joel@example.com');
    await fireEvent.changeText(screen.getByTestId('mobile-login-password'), 'wrong');

    await userEvent.press(screen.getByTestId(SUBMIT));

    expect(await screen.findByTestId('mobile-login-error')).toHaveTextContent(
      'Incorrect email or password, or the account is locked for now.',
    );
    expect(screen.getByTestId(SERVER)).toHaveProp('value', 'https://codex.test');
    expect(screen.getByTestId(SUBMIT)).toBeEnabled();
  });
});
