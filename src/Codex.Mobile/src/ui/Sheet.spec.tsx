import { fireEvent, screen } from '@testing-library/react-native';
import { Text } from 'react-native';
import { renderScreen } from '~/test-support/renderScreen';
import { Sheet } from './Sheet';

describe('Sheet', () => {
  it('ShouldShowTitleAndContent', async () => {
    await renderScreen(
      <Sheet title="Scene" onClose={jest.fn()} testID="s">
        <Text>Body</Text>
      </Sheet>,
    );

    expect(screen.getByTestId('s-title')).toHaveTextContent('Scene');
    expect(screen.getByText('Body')).toBeOnTheScreen();
    expect(screen.queryByTestId('s-dock')).toBeNull();
  });

  it.each(['s-close', 's-backdrop'])('Pressing%s_ShouldClose', async (id) => {
    const onClose = jest.fn();
    await renderScreen(
      <Sheet title="Scene" onClose={onClose} testID="s">
        <Text>Body</Text>
      </Sheet>,
    );

    await fireEvent.press(screen.getByTestId(id));

    expect(onClose).toHaveBeenCalledTimes(1);
  });

  it('Dock_ShouldPutThePrimaryOnTheRight', async () => {
    const onPrimary = jest.fn();
    const onSecondary = jest.fn();
    await renderScreen(
      <Sheet
        title="Record"
        onClose={jest.fn()}
        dock={{
          secondary: { label: 'Discard', onPress: onSecondary },
          primary: { label: 'Stop', onPress: onPrimary },
        }}
        testID="s"
      >
        <Text>Body</Text>
      </Sheet>,
    );

    await fireEvent.press(screen.getByTestId('s-primary'));
    await fireEvent.press(screen.getByTestId('s-secondary'));

    expect(onPrimary).toHaveBeenCalledTimes(1);
    expect(onSecondary).toHaveBeenCalledTimes(1);
    const labels = screen
      .getByTestId('s-dock')
      .children.map((child) => (typeof child === 'string' ? child : child.props.testID));
    expect(labels).toEqual(['s-secondary', 's-primary']);
  });
});
