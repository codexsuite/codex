import { createContext, useContext, type ReactNode } from 'react';
import { realSpeechEngine } from './realSpeechEngine';
import type { SpeechEngine } from './SpeechEngine';

const SpeechEngineContext = createContext<SpeechEngine>(realSpeechEngine);

/** Supplies a different engine to the screens below (a spec gives it the fake). The app mounts none: it uses the real one. */
export function SpeechEngineProvider({
  engine,
  children,
}: {
  engine: SpeechEngine;
  children: ReactNode;
}) {
  return <SpeechEngineContext.Provider value={engine}>{children}</SpeechEngineContext.Provider>;
}

/** The speech engine for this screen. */
export function useSpeechEngine(): SpeechEngine {
  return useContext(SpeechEngineContext);
}
