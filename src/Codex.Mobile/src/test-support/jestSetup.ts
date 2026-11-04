// Runs before every *.spec.tsx (package.json → jest.setupFilesAfterEnv): the native modules the app reads at import
// time are replaced by in-memory fakes, and the shared state is reset after each test.
import { configureMobilePlatform } from '../mobilePlatform';
import { queryClient } from '../queryClient';
import useColorScheme from 'react-native/Libraries/Utilities/useColorScheme';
import { liveAutosaves } from '../book/liveAutosaves';
import { bookSaveStatus } from '../book/saveStatus';
import { audioHarness } from './mocks/mockExpoAudio';
import { webViewHarness } from './mocks/reactNativeWebview';

// CI runners are slower than a dev machine, and a screen test that renders the app can take longer
// than jest's 5 s default; which test times out changes from run to run.
jest.setTimeout(20_000);

jest.mock('expo-sqlite/kv-store', () => {
  // Starts signed in to https://codex.test; the secret is in the secure store below.
  const values = new Map<string, string>([
    ['codex.server', 'https://codex.test'],
    ['codex.email', 'jd@codex.local'],
  ]);
  return {
    __esModule: true,
    default: {
      getItemSync: (key: string) => values.get(key) ?? null,
      setItemSync: (key: string, value: string) => void values.set(key, value),
      removeItemSync: (key: string) => void values.delete(key),
    },
  };
});

jest.mock('expo-secure-store', () => ({
  getItem: () => 'cdx_dev_test_secret',
  setItem: () => {},
  deleteItemAsync: async () => {},
}));

jest.mock('@react-native-community/netinfo', () => ({
  __esModule: true,
  default: { addEventListener: () => () => {}, fetch: async () => ({ isConnected: true }) },
}));

// The device colour scheme, set per test by renderScreen / renderApp (`scheme`).
jest.mock('react-native/Libraries/Utilities/useColorScheme', () => ({
  __esModule: true,
  default: jest.fn(() => 'light'),
}));

jest.mock('react-native-webview', () => jest.requireActual('./mocks/reactNativeWebview'));
// Real expo-audio cannot load under jest; any spec that renders the record route needs this.
jest.mock('expo-audio', () => jest.requireActual('./mocks/mockExpoAudio'));

// The core reads the server and the secret through the platform; the mocks above make that jd@codex.local on https://codex.test.
configureMobilePlatform();

// A failed query should fail the spec now, not after the app's retry delay.
queryClient.setDefaultOptions({ queries: { retry: false, gcTime: Infinity } });

beforeEach(() => {
  jest.mocked(useColorScheme).mockReturnValue('light');
  queryClient.clear();
  webViewHarness.reset();
  audioHarness.reset();
  liveAutosaves.reset();
  bookSaveStatus.reset();
});

afterEach(() => {
  jest.restoreAllMocks();
});
