import { fireEvent, screen, userEvent } from '@testing-library/react-native';
import { renderScreen } from '~/test-support/renderScreen';
import { Row } from './Row';

describe('Row', () => {
  it('ShouldShowLabelValueDetailAndChip', async () => {
    await renderScreen(
      <Row label="Status" value="Writing" detail="Where the book is" chip="Series" testID="r" />,
    );

    expect(screen.getByText('Status')).toBeOnTheScreen();
    expect(screen.getByTestId('r-value')).toHaveTextContent('Writing');
    expect(screen.getByText('Where the book is')).toBeOnTheScreen();
    expect(screen.getByTestId('r-chip')).toHaveTextContent('Series');
  });

  it('WithOnPress_ShouldBeAButtonWithAChevron', async () => {
    const onPress = jest.fn();
    await renderScreen(<Row label="Status" onPress={onPress} testID="r" />);

    await fireEvent.press(screen.getByTestId('r'));

    expect(onPress).toHaveBeenCalledTimes(1);
    expect(screen.getByText('›')).toBeOnTheScreen();
  });

  it('WhenDisabled_ShouldIgnoreTapsAndHideTheChevron', async () => {
    const onPress = jest.fn();
    await renderScreen(
      <Row label="Layout & print" chip="Desktop" disabled onPress={onPress} testID="r" />,
    );

    await userEvent.press(screen.getByTestId('r'));

    expect(onPress).not.toHaveBeenCalled();
    expect(screen.queryByText('›')).toBeNull();
  });

  it('Danger_ShouldColourTheLabel', async () => {
    await renderScreen(<Row label="Archive book" danger onPress={jest.fn()} />);

    expect(screen.getByText('Archive book')).toHaveStyle({ color: '#c2353b' });
  });
});
