import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { render, type RenderResult } from '@testing-library/react-native';
import { renderRouter } from 'expo-router/testing-library';
import type { ReactElement } from 'react';
import useColorScheme from 'react-native/Libraries/Utilities/useColorScheme';
import { SafeAreaProvider } from 'react-native-safe-area-context';
import { SpeechEngineProvider } from '../speech';
import type { SpeechEngine } from '../speech';
import { FAKE_BOOK_ID, worldRoutes } from './fakes/routes';
import { installFakeApi, type FakeApi, type FakeHandler } from './fakeApi';

/** The device colour scheme for this test (reset before each test); the theme follows it. */
function setScheme(scheme: 'light' | 'dark' | undefined) {
  if (scheme !== undefined) jest.mocked(useColorScheme).mockReturnValue(scheme);
}

const clients: QueryClient[] = [];
afterEach(() => {
  clients.splice(0).forEach((client) => client.clear());
});

const phone = {
  frame: { x: 0, y: 0, width: 390, height: 844 },
  insets: { top: 47, left: 0, right: 0, bottom: 34 },
};

/**
 * Renders one component the way a screen is rendered: safe-area insets of a notched phone, a react-query client of its
 * own (no retries), and the theme from the device colour scheme. For a component that is not a route (a primitive, a
 * lane's widget). The network is whatever `installFakeApi` last installed.
 */
export async function renderScreen(
  ui: ReactElement,
  { scheme, speechEngine }: { scheme?: 'light' | 'dark'; speechEngine?: SpeechEngine } = {},
): Promise<RenderResult & { queryClient: QueryClient }> {
  setScheme(scheme);
  // No retries, and no garbage-collection timers (the default 5 minutes would keep jest from exiting).
  const queryClient = new QueryClient({
    defaultOptions: {
      queries: { retry: false, gcTime: Infinity },
      mutations: { gcTime: Infinity },
    },
  });
  clients.push(queryClient);
  const result = await render(
    <SafeAreaProvider initialMetrics={phone}>
      <QueryClientProvider client={queryClient}>
        {speechEngine ? (
          <SpeechEngineProvider engine={speechEngine}>{ui}</SpeechEngineProvider>
        ) : (
          ui
        )}
      </QueryClientProvider>
    </SafeAreaProvider>,
  );
  return Object.assign(result, { queryClient });
}

export interface RenderAppOptions {
  /** Where the app opens, e.g. `/book/book-1/plan` (see links.ts). Default `/`, the Library. */
  url?: string;
  /** Routes for the fake network, on top of `worldRoutes()` (or instead of it with `world: false`). */
  api?: Record<string, FakeHandler>;
  world?: boolean;
  /** The device colour scheme (default light). */
  scheme?: 'light' | 'dark';
  /** Supplies this engine to every screen (default: the real one, which is not built yet). */
  speechEngine?: SpeechEngine;
}

/**
 * Renders the real app (`app/`, signed in as jd@codex.local to https://codex.test) at `url`, with the fake network
 * installed. Use `getPathname()` / `screen` from the result; `api` records the requests the screens made.
 */
export async function renderApp({
  url = '/',
  api = {},
  world = true,
  scheme,
  speechEngine,
}: RenderAppOptions = {}) {
  setScheme(scheme);
  const fakeApi: FakeApi = installFakeApi({ ...(world ? worldRoutes() : {}), ...api });
  const router = await renderRouter('./app', {
    initialUrl: url,
    wrapper: ({ children }) =>
      speechEngine ? (
        <SpeechEngineProvider engine={speechEngine}>{children}</SpeechEngineProvider>
      ) : (
        children
      ),
  });
  return Object.assign(router, { api: fakeApi, bookId: FAKE_BOOK_ID });
}
