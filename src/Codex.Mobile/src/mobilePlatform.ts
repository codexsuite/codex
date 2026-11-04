import {
  configurePlatform,
  memoryStore,
  type KeyValueStore,
  type Platform,
} from '@codex/core/platform';
import NetInfo from '@react-native-community/netinfo';
import Storage from 'expo-sqlite/kv-store';
import { onSessionLost } from './session/auth';
import { sessionStore } from './session/session';

// What @codex/core needs from the phone (#35): storage, connectivity, and where requests go and how they sign in.

const kvStore: KeyValueStore = {
  getItem: (key) => Storage.getItemSync(key),
  setItem: (key, value) => Storage.setItemSync(key, value),
  removeItem: (key) => void Storage.removeItemSync(key),
};

// NetInfo answers asynchronously; keep the latest answer for the synchronous `isOffline`. Online until told otherwise.
let offline = false;
const onlineListeners = new Set<() => void>();
NetInfo.addEventListener((state) => {
  const wasOffline = offline;
  offline = state.isConnected === false;
  if (wasOffline && !offline) onlineListeners.forEach((listener) => listener());
});

export const mobilePlatform: Platform = {
  persistentStore: () => kvStore,
  sessionStore: memoryStore,
  isOffline: () => offline,
  onOnline(listener) {
    onlineListeners.add(listener);
    return () => void onlineListeners.delete(listener);
  },
  // The gate in app/_layout.tsx shows the sign-in screen as soon as the secret is gone.
  onUnauthorized: () => void onSessionLost(),
  apiOrigin: () => sessionStore.credentials().origin ?? '',
  authHeaders(): Record<string, string> {
    const { secret } = sessionStore.credentials();
    return secret ? { Authorization: `Bearer ${secret}` } : {};
  },
};

export const configureMobilePlatform = () => configurePlatform(mobilePlatform);
