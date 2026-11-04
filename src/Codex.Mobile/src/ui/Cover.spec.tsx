import { screen } from '@testing-library/react-native';
import { renderScreen } from '~/test-support/renderScreen';
import { Cover } from './Cover';

describe('Cover', () => {
  it('ShouldDrawTheGeneratedCoverInThePalette', async () => {
    await renderScreen(<Cover palette="cover-5" title="Forsaken" testID="c" />);

    expect(screen.getByTestId('c')).toHaveStyle({
      backgroundColor: '#3b0d1f',
      width: 30,
      height: 45,
    });
    expect(screen.getByText('Forsaken')).toHaveStyle({ color: '#fbcfe8' });
  });

  it('WithAWidth_ShouldKeepTheTwoToThreeProportion', async () => {
    await renderScreen(<Cover palette="cover-1" title="T" width={96} testID="c" />);

    expect(screen.getByTestId('c')).toHaveStyle({ width: 96, height: 144 });
  });

  it('WithAnUploadedImage_ShouldLoadItFromTheServerWithTheDeviceSecret', async () => {
    await renderScreen(<Cover palette="cover-1" title="T" imageUrl="/api/assets/a1" testID="c" />);

    expect(screen.getByTestId('c').props.source).toEqual({
      uri: 'https://codex.test/api/assets/a1',
      headers: { Authorization: 'Bearer cdx_dev_test_secret' },
    });
  });

  it('WithAnUnknownPalette_ShouldShowAVisibleMarkerInsteadOfCrashing', async () => {
    await renderScreen(<Cover palette="cover-99" title="T" testID="c" />);

    expect(screen.getByTestId('c')).toHaveTextContent('?');
  });
});
