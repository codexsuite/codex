import { fireEvent, screen, userEvent } from '@testing-library/react-native';
import { renderScreen } from '~/test-support/renderScreen';
import { Button } from './Button';

describe('Button', () => {
  it('Press_ShouldCallOnPress', async () => {
    const onPress = jest.fn();
    await renderScreen(<Button label="Save" onPress={onPress} testID="b" />);

    await fireEvent.press(screen.getByTestId('b'));

    expect(onPress).toHaveBeenCalledTimes(1);
    expect(screen.getByText('Save')).toBeOnTheScreen();
  });

  it.each([
    ['disabled', { disabled: true }],
    ['busy', { busy: true }],
  ])('Press_WhenItIs%s_ShouldDoNothing', async (_name, state) => {
    const onPress = jest.fn();
    await renderScreen(<Button label="Save" onPress={onPress} testID="b" {...state} />);

    await userEvent.press(screen.getByTestId('b'));

    expect(onPress).not.toHaveBeenCalled();
  });

  it('Primary_InTheDarkTheme_ShouldUseTheDarkAccent', async () => {
    await renderScreen(<Button label="Go" variant="primary" onPress={jest.fn()} testID="b" />, {
      scheme: 'dark',
    });

    expect(screen.getByTestId('b')).toHaveStyle({ backgroundColor: '#5cc8f0' });
  });

  it('Danger_ShouldShowTheLabelInTheDangerColour', async () => {
    await renderScreen(<Button label="Delete" variant="danger" onPress={jest.fn()} />);

    expect(screen.getByText('Delete')).toHaveStyle({ color: '#c2353b' });
  });
});
