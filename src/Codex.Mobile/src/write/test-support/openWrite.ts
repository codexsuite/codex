import { encodePageMessage, type PageMessage } from '@codex/core/editing/editorBridge';
import { storeUnsavedProse } from '@codex/core/api/write';
import { act, screen, waitFor } from '@testing-library/react-native';
import type { FakeHandler } from '~/test-support/fakeApi';
import { webViewHarness } from '~/test-support/mocks/reactNativeWebview';
import { renderApp } from '~/test-support/renderScreen';
import { writeTestIds } from '../testIds';
import { messageEvent } from './events';
import { writeRoutes } from './world';

// Drafts of unsaved text are kept in the phone's persistent store, which outlives a test: forget them after each one.
afterEach(() => {
  for (const sceneId of ['s1', 's2', 's3', 's4', 's9']) storeUnsavedProse(sceneId, null);
});

type Load = Extract<
  ReturnType<ReturnType<typeof webViewHarness.last>['sentToPage']>[number],
  { type: 'load' }
>;

/** Opens the Write tab on the test book and waits for the editor page to be mounted (not yet `ready`). */
export async function openWrite({
  url = '/book/book-1/write',
  api = {},
}: { url?: string; api?: Record<string, FakeHandler> } = {}) {
  const app = await renderApp({ url, api: { ...writeRoutes(), ...api } });
  await screen.findByTestId(writeTestIds.webView);
  return { app, page: pageOf() };
}

/** The page as a spec plays it: say something to the app, read what the app sent. */
export function pageOf() {
  const sent = () => webViewHarness.last().sentToPage();
  return {
    say: (message: PageMessage) => sayFromPage(encodePageMessage(message)),
    sayRaw: (data: string) => sayFromPage(data),
    sayFrom: (message: PageMessage, url: string) => sayFromPage(encodePageMessage(message), url),
    ready: () => sayFromPage(encodePageMessage({ type: 'ready', v: 1 })),
    sent,
    loads: (): Load[] => sent().filter((m): m is Load => m.type === 'load'),
    waitForLoads: async (count: number) => {
      await waitFor(() => expect(sent().filter((m) => m.type === 'load')).toHaveLength(count));
    },
  };
}

/**
 * A message from the editor page: the harness's own `emitFromPage` reports `about:blank` as the sender, and the app only
 * listens to the editor page's address.
 */
async function sayFromPage(data: string, url?: string) {
  await act(async () => {
    webViewHarness.last().props.onMessage?.(messageEvent(data, url));
  });
}
