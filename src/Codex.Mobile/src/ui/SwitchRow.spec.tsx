import { fireEvent, screen } from '@testing-library/react-native';
import { renderScreen } from '~/test-support/renderScreen';
import { SwitchRow } from './SwitchRow';

describe('SwitchRow', () => {
  it('Toggle_ShouldReportTheNewValue', async () => {
    const onValueChange = jest.fn();
    await renderScreen(
      <SwitchRow
        label="Always in context"
        description="Sent with every prompt"
        value={false}
        onValueChange={onValueChange}
        testID="s"
      />,
    );

    await fireEvent(screen.getByTestId('s-switch'), 'valueChange', true);

    expect(onValueChange).toHaveBeenCalledWith(true);
    expect(screen.getByText('Sent with every prompt')).toBeOnTheScreen();
  });
});
