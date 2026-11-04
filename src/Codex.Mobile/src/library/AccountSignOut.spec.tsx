import { fireEvent, screen, waitFor } from '@testing-library/react-native';
import { worldRoutes } from '~/test-support/fakes';
import { renderApp } from '~/test-support/renderScreen';
import { libraryTestIds as ids } from './testIds';
import { me } from './test-support/fakes';

// Its own file: signing out clears the app's session for the rest of the file.
describe('Account sheet Sign out', () => {
  it('ShouldEndTheDeviceSessionAndReturnToSignInWithTheServerFilled', async () => {
    const app = await renderApp({
      api: {
        ...worldRoutes(),
        'GET /api/auth/me': me(),
        'DELETE /api/auth/device-sessions/current': undefined,
      },
    });

    await fireEvent.press(await screen.findByTestId(ids.avatar));
    await fireEvent.press(await screen.findByTestId(ids.accountSignOut));

    expect(await screen.findByTestId('mobile-login-server')).toHaveProp(
      'value',
      'https://codex.test',
    );
    await waitFor(() =>
      expect(app.api.callsTo('DELETE /api/auth/device-sessions/current')).toHaveLength(1),
    );
    expect(screen.queryByTestId(ids.accountSheet)).toBeNull();
  });
});
