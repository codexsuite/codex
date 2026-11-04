import { ApiRequestError } from '@codex/core/api/client';
import { fireEvent, screen } from '@testing-library/react-native';
import { renderScreen } from '~/test-support/renderScreen';
import { ErrorState } from './ErrorState';

describe('ErrorState', () => {
  it('WithAnApiError_ShouldShowItsMessageAndRetry', async () => {
    const onRetry = jest.fn();
    await renderScreen(
      <ErrorState
        error={new ApiRequestError('No access.', 403, 'forbidden')}
        fallback="Couldn't load."
        onRetry={onRetry}
        testID="x"
      />,
    );

    await fireEvent.press(screen.getByTestId('x-retry'));

    expect(screen.getByTestId('x')).toHaveTextContent('No access.');
    expect(onRetry).toHaveBeenCalledTimes(1);
  });

  it('WithAnyOtherError_ShouldShowTheFallback', async () => {
    await renderScreen(
      <ErrorState
        error={new TypeError('boom')}
        fallback="Couldn't load."
        onRetry={jest.fn()}
        testID="x"
      />,
    );

    expect(screen.getByTestId('x')).toHaveTextContent("Couldn't load.");
  });
});
