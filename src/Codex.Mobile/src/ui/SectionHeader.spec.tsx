import { fireEvent, screen } from '@testing-library/react-native';
import { renderScreen } from '~/test-support/renderScreen';
import { SectionHeader } from './SectionHeader';

describe('SectionHeader', () => {
  it('ShouldShowTitleCountAndAction', async () => {
    const onPress = jest.fn();
    await renderScreen(
      <SectionHeader title="Aliases" count={2} action={{ label: '+ Alias', onPress }} testID="h" />,
    );

    await fireEvent.press(screen.getByTestId('h-action'));

    expect(onPress).toHaveBeenCalledTimes(1);
    expect(screen.getByRole('header')).toHaveTextContent('Aliases');
    expect(screen.getByTestId('h')).toHaveTextContent('Aliases2+ Alias');
  });
});
