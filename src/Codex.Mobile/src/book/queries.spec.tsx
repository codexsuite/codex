import { screen, waitFor } from '@testing-library/react-native';
import { Text } from 'react-native';
import { installFakeApi } from '~/test-support/fakeApi';
import { FAKE_BOOK_ID, worldRoutes } from '~/test-support/fakes';
import { renderScreen } from '~/test-support/renderScreen';
import { useBookHeader, useBookSettings, useKb, useManuscript, useOutline } from './queries';

function Probe() {
  const header = useBookHeader(FAKE_BOOK_ID).data?.title;
  const scenes = useOutline(FAKE_BOOK_ID).data?.acts[0]?.chapters[0]?.scenes.length;
  const entries = useKb(FAKE_BOOK_ID).data?.entries.length;
  const prose = useManuscript(FAKE_BOOK_ID).data?.scenes.length;
  const target = useBookSettings(FAKE_BOOK_ID).data?.targetWords;
  return <Text testID="probe">{[header, scenes, entries, prose, target].join('|')}</Text>;
}

describe('book queries', () => {
  it('ShouldReadEachResourceFromItsEndpoint', async () => {
    const api = installFakeApi(worldRoutes());
    await renderScreen(<Probe />);

    await waitFor(() =>
      expect(screen.getByTestId('probe')).toHaveTextContent('The Empty Seat|1|1|1|90000'),
    );
    expect(api.calls.map((call) => call.path).sort()).toEqual([
      '/api/books/book-1',
      '/api/books/book-1/kb',
      '/api/books/book-1/manuscript',
      '/api/books/book-1/outline',
      '/api/books/book-1/settings',
    ]);
  });
});
