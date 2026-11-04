import { fireEvent, screen } from '@testing-library/react-native';
import { View } from 'react-native';
import { renderScreen } from '~/test-support/renderScreen';
import { Chip } from './Chip';

describe('Chip', () => {
  it('WithoutOnPress_ShouldBeAPlainLabelWithItsCount', async () => {
    await renderScreen(<Chip label="Characters" count={12} testID="c" />);

    expect(screen.getByTestId('c')).toHaveTextContent('Characters12');
    expect(screen.queryByRole('button')).toBeNull();
  });

  it('WithOnPress_ShouldBeAButtonThatKnowsIfItIsSelected', async () => {
    const onPress = jest.fn();
    await renderScreen(<Chip label="All" selected onPress={onPress} testID="c" />);

    await fireEvent.press(screen.getByTestId('c'));

    expect(onPress).toHaveBeenCalledTimes(1);
    expect(screen.getByTestId('c')).toBeSelected();
  });

  it('WithLeading_ShouldDrawItBeforeTheLabel', async () => {
    await renderScreen(<Chip label="drafting" leading={<View testID="dot" />} testID="c" />);

    const children = screen.getByTestId('c').children;
    expect(screen.getByTestId('dot')).toBeOnTheScreen();
    expect(children.indexOf(screen.getByTestId('dot') as never)).toBeLessThan(
      children.indexOf(screen.getByText('drafting') as never),
    );
  });
});
