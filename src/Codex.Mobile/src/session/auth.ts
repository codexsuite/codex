import { createDeviceSession, endCurrentDeviceSession, meKey } from '@codex/core/api/auth';
import * as Device from 'expo-device';
import { queryClient } from '../queryClient';
import { sessionStore } from './session';
import { lostSession, signIn, signOut, type SignInDeps } from './signIn';

// The sign-in flow bound to the app's real session, network calls and query cache.
const deps: SignInDeps = {
  store: sessionStore,
  createDeviceSession,
  endCurrentDeviceSession,
  deviceName: () => Device.deviceName ?? Device.modelName ?? 'Phone',
  seedMe: (me) => queryClient.setQueryData(meKey, me),
  clearCache: () => queryClient.clear(),
};

export const signInToServer = (input: { server: string; email: string; password: string }) =>
  signIn(deps, input);
export const signOutOfServer = () => signOut(deps);
export const onSessionLost = () => lostSession(deps);
