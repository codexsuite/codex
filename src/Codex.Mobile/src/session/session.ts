import Storage from 'expo-sqlite/kv-store';
import * as SecureStore from 'expo-secure-store';
import { useSyncExternalStore } from 'react';
import { createSessionStore, type SessionState } from './sessionStore';

const SECRET_KEY = 'codex.device-secret';

// The app's one session. Created at module load: expo-sqlite's key-value store and expo-secure-store both read
// synchronously, so the first render already knows whether the user is signed in.
export const sessionStore = createSessionStore({
  readKv: (key) => Storage.getItemSync(key),
  writeKv: (key, value) => Storage.setItemSync(key, value),
  readSecret: () => SecureStore.getItem(SECRET_KEY),
  writeSecret: (value) => SecureStore.setItem(SECRET_KEY, value),
  deleteSecret: () => SecureStore.deleteItemAsync(SECRET_KEY),
});

export function useSession(): SessionState {
  return useSyncExternalStore(sessionStore.subscribe, sessionStore.getState);
}
