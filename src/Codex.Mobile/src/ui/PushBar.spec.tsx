import { fireEvent, screen } from '@testing-library/react-native';
import { renderScreen } from '~/test-support/renderScreen';
import { PushBar } from './PushBar';

describe('PushBar', () => {
  it('ShouldShowTheParentTheTitleAndTheAction', async () => {
    const onBack = jest.fn();
    const onDone = jest.fn();
    await renderScreen(
      <PushBar
        parent="Settings"
        title="Style guide"
        onBack={onBack}
        action={{ label: 'Done', onPress: onDone }}
        testID="p"
      />,
    );

    await fireEvent.press(screen.getByTestId('p-back'));
    await fireEvent.press(screen.getByTestId('p-action'));

    expect(screen.getByTestId('p-title')).toHaveTextContent('Style guide');
    expect(screen.getByText('Settings')).toBeOnTheScreen();
    expect(onBack).toHaveBeenCalledTimes(1);
    expect(onDone).toHaveBeenCalledTimes(1);
  });

  it('WithoutAnAction_ShouldShowNone', async () => {
    await renderScreen(<PushBar parent="Knowledge" title="Ana" testID="p" />);

    expect(screen.queryByTestId('p-action')).toBeNull();
  });
});
