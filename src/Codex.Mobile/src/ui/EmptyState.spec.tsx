import { fireEvent, screen } from '@testing-library/react-native';
import { renderScreen } from '~/test-support/renderScreen';
import { EmptyState } from './EmptyState';

describe('EmptyState', () => {
  it('ShouldShowTheMessageAndRunItsAction', async () => {
    const onPress = jest.fn();
    await renderScreen(
      <EmptyState
        title="No entries yet"
        message="Add the first one."
        action={{ label: 'New entry', onPress }}
        testID="e"
      />,
    );

    await fireEvent.press(screen.getByTestId('e-action'));

    expect(onPress).toHaveBeenCalledTimes(1);
    expect(screen.getByText('No entries yet')).toBeOnTheScreen();
    expect(screen.getByText('Add the first one.')).toBeOnTheScreen();
  });
});
