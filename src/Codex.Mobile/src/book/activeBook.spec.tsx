import { saveStatusStore } from '@codex/core/editing/saveStatusStore';
import { act, screen } from '@testing-library/react-native';
import { router } from 'expo-router';
import { planLink } from '~/links';
import { renderApp } from '~/test-support/renderScreen';
import { shellTestIds } from '~/ui';
import { bookSaveStatus } from './saveStatus';

describe('The book the save dot reports for', () => {
  it('Dot_WhenAnotherBookIsOpenedOnTopAndClosed_ShouldReturnToTheFirstBooksState', async () => {
    await renderApp({ url: '/book/book-1/plan' });
    await screen.findByTestId(shellTestIds.saveDot);
    bookSaveStatus.forBook('book-1').store('kb:a').set('retry');
    bookSaveStatus.forBook('book-2').store('kb:b').set('offline');
    expect(saveStatusStore.getState()).toBe('retry');

    await act(async () => router.push(planLink('book-2')));
    expect(saveStatusStore.getState()).toBe('offline');

    await act(async () => router.back());
    expect(saveStatusStore.getState()).toBe('retry');
  });
});
