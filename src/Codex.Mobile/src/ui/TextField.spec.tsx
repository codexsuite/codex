import { fireEvent, screen } from '@testing-library/react-native';
import { renderScreen } from '~/test-support/renderScreen';
import { TextField } from './TextField';

describe('TextField', () => {
  it('Typing_ShouldCallOnChangeText', async () => {
    const onChangeText = jest.fn();
    await renderScreen(<TextField label="Title" value="" onChangeText={onChangeText} testID="t" />);

    await fireEvent.changeText(screen.getByTestId('t'), 'Hello');

    expect(onChangeText).toHaveBeenCalledWith('Hello');
    expect(screen.getByText('Title')).toBeOnTheScreen();
  });

  it('WithAnError_ShouldShowItUnderTheInput', async () => {
    await renderScreen(<TextField value="" error="Required" testID="t" />);

    expect(screen.getByTestId('t-error')).toHaveTextContent('Required');
  });

  it('Multiline_ShouldBeAMultilineInput', async () => {
    await renderScreen(<TextField value="" multiline testID="t" />);

    expect(screen.getByTestId('t').props.multiline).toBe(true);
  });
});
