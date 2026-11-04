import { fireEvent, screen, waitFor } from '@testing-library/react-native';
import Login from '../../app/login';
import { installFakeApi } from '~/test-support/fakeApi';
import { renderScreen } from '~/test-support/renderScreen';
import { me } from '~/library/test-support/fakes';

// Its own file: a successful sign-in changes the stored session for the rest of the file.
describe('Sign in from the password field', () => {
  it('ShouldCheckTheNewAddressFirstThenSignInToIt', async () => {
    const api = installFakeApi({
      'GET /api/health': { status: 'ok', version: '1.0.0+abc123' },
      'POST /api/auth/device-sessions': { secret: 'cdx_new', me: me() },
    });
    await renderScreen(<Login />);
    await fireEvent.changeText(
      await screen.findByTestId('mobile-login-server'),
      'codex.example.com',
    );
    await fireEvent.changeText(screen.getByTestId('mobile-login-email'), 'joel@example.com');
    await fireEvent.changeText(screen.getByTestId('mobile-login-password'), 'hunter2');

    await fireEvent(screen.getByTestId('mobile-login-password'), 'submitEditing');

    await waitFor(() => expect(api.callsTo('POST /api/auth/device-sessions')).toHaveLength(1));
    const urls = (globalThis.fetch as jest.Mock).mock.calls.map((call) => String(call[0]));
    expect(urls).toContain('https://codex.example.com/api/health');
    expect(urls.at(-1)).toBe('https://codex.example.com/api/auth/device-sessions');
    expect(api.callsTo('POST /api/auth/device-sessions')[0].body).toMatchObject({
      email: 'joel@example.com',
      password: 'hunter2',
    });
  });
});
